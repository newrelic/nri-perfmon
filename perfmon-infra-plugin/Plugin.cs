
using System;
using System.Collections.Generic;
using System.Management;
using System.Diagnostics;
using NLog;
using Newtonsoft.Json;

namespace newrelic_infra_perfmon_plugin
{
    // Config file classes

    public class Counter
    {
        public string counter { get; set; }
        public string unit { get; set; }
    }

        public class Counterlist
        {
            public string provider { get; set; }
            public string category { get; set; }
            public string instance { get; set; }
            public string counter { get; set; }
            public string unit { get; set; }
            public List<Counter> counters { get; set; }
        }

        public class Config
        {
            public string name { get; set; }
            public List<Counterlist> counterlist { get; set; }
        }

        // Output format

        public class Output
        {
            public string name { get; set; }
            public string protocol_version { get; set; }
            public string integration_version { get; set; }
            public List<Dictionary<string, Object>> events { get; set; }
            public Dictionary<string, string> inventory { get; set; }
            public List<Dictionary<string, Object>> metrics { get; set; }
        }

        // The rest

        class PerfmonQuery
        {
            public PerfmonQuery(string pname, string caname, string coname, string iname, string munit)
            {
                if (String.Equals(pname, "PerfCounter"))
                {
                    if (String.IsNullOrEmpty(iname))
                    {
                        metricName = string.Format("{0}/{1}", caname, coname);
                        counterOrQuery = new PerformanceCounter(caname, coname);
                    }
                    else
                    {
                        metricName = string.Format("{0}({2})/{1}", caname, coname, iname);
                        counterOrQuery = new PerformanceCounter(caname, coname, iname);
                    }
                }
                else
                {
                    metricName = string.Format("{0}", caname);
                    if (String.IsNullOrEmpty(iname))
                    {
                        counterOrQuery = string.Format("Select Name, {0} from Win32_PerfFormattedData_{1}_{2}", coname, pname, caname);
                    }
                    else
                    {
                        counterOrQuery = string.Format("Select Name, {0} from Win32_PerfFormattedData_{1}_{2} Where Name Like '{3}'", coname, pname, caname, iname);
                    }
                }
                metricUnit = munit;
            }

            // Private setters to enforce that they only get set by constructor
            public string metricName { get; private set; }
            public string metricUnit { get; private set; }
            public object counterOrQuery { get; private set; }
        }

        public class PerfmonInfraService
        {
            private static Logger logger = LogManager.GetCurrentClassLogger();
            private static string DefaultUnit = "count";

            private string Name { get; set; }
            private List<PerfmonQuery> PerfmonQueries { get; set; }
            private ManagementScope Scope { get; set; }
            private Dictionary<string, Object> Metrics = new Dictionary<string, Object>();
            private Output output = new Output();

            public PerfmonInfraService(string compname, List<Counterlist> counters)
            {
                PerfmonQueries = new List<PerfmonQuery>();
                Name = compname;

                output.name = compname;
                output.protocol_version = "1";
                output.integration_version = "0.1.0";
                Scope = new ManagementScope("\\\\" + Name + "\\root\\cimv2");

                int whichCounter = -1;
                foreach (Counterlist aCounter in counters)
                {
                    whichCounter++;
                    if (String.IsNullOrEmpty(aCounter.provider) || String.IsNullOrEmpty(aCounter.category))
                    {
                        logger.Error("plugin.json contains malformed counter: counterlist[{0}] missing 'provider' or 'category'. Please review and compare to template.", whichCounter);
                        continue;
                    }

                    string instanceName = string.Empty;
                    if (!String.IsNullOrEmpty(aCounter.instance) && !String.Equals(aCounter.instance, "*"))
                    {
                        instanceName = aCounter.instance.ToString();
                    }

                    if (aCounter.counters != null)
                    {
                        int whichSubCounter = -1;
                        foreach (var aSubCounter in aCounter.counters)
                        {
                            whichSubCounter++;
                            if (String.IsNullOrEmpty(aSubCounter.counter))
                            {
                                logger.Error("plugin.json contains malformed counter: 'counters' in counterlist[{0}] missing 'counter' in element {1}. Please review and compare to template.", whichCounter, whichSubCounter);
                                continue;
                            }
                            string metricUnit = aSubCounter.unit ?? DefaultUnit;
                            PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, aSubCounter.counter, instanceName, metricUnit));
                        }
                    }
                    else if (!String.IsNullOrEmpty(aCounter.counter))
                    {
                        string metricUnit = aCounter.unit ?? DefaultUnit;
                        PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, aCounter.counter, instanceName, metricUnit));
                    }
                    else
                    {
                        logger.Error("plugin.json contains malformed counter: counterlist[{0}] missing 'counter' or 'counters'. Please review and compare to template.", whichCounter);
                        continue;
                    }
                }
            }

            public void PollCycle()
            {
                Metrics.Add("event_type", "PerfmonMetrics");
                var metricNames = new Dictionary<string, int>();
                foreach (var thisQuery in PerfmonQueries)
                {
                    try
                    {
                        if (thisQuery.counterOrQuery is PerformanceCounter)
                        {
                            try
                            {
                                float value = ((PerformanceCounter)thisQuery.counterOrQuery).NextValue();
                                string metricName = thisQuery.metricName;
                                if (!float.IsNaN(value))
                                {
                                    if (metricNames.ContainsKey(metricName))
                                    {
                                        metricName = metricName + "#" + metricNames[metricName]++;
                                    }
                                    else
                                    {
                                        metricNames.Add(metricName, 1);
                                    }
                                    logger.Debug("{0}/{1}: {2} {3}", Name, metricName, value, thisQuery.metricUnit);
                                    RecordMetric(metricName, thisQuery.metricUnit, value);
                                }
                            }
                            catch (Exception e)
                            {
                                logger.Error("Exception occurred in processing next value. {0}\r\n{1}", e.Message, e.StackTrace);
                            }
                        }
                        else if (thisQuery.counterOrQuery is string)
                        {
                            try
                            {
                                if (!Scope.IsConnected)
                                {
                                    Scope.Connect();
                                }
                            }
                            catch (Exception e)
                            {
                                logger.Error("Unable to connect to \"{0}\". {1}", Name, e.Message);
                                continue;
                            }

                            var queryResults = (new ManagementObjectSearcher(Scope, new ObjectQuery((string)thisQuery.counterOrQuery))).Get();
                            foreach (ManagementObject result in queryResults)
                            {
                                string thisInstanceName = string.Empty;
                                if (result["Name"] != null)
                                {
                                    thisInstanceName = string.Format("({0})", result["Name"]);
                                }

                                foreach (PropertyData prop in result.Properties)
                                {
                                    if (prop.Name == "Name")
                                        continue;

                                    float value = Convert.ToSingle(prop.Value);
                                    string metricName = string.Format("{0}{1}/{2}", thisQuery.metricName, thisInstanceName, prop.Name);

                                    if (metricNames.ContainsKey(metricName))
                                    {
                                        metricName = metricName + "#" + metricNames[metricName]++;
                                    }
                                    else
                                    {
                                        metricNames.Add(metricName, 1);
                                    }
                                    if (!float.IsNaN(value))
                                    {
                                        logger.Debug("{0}/{1}: {2} {3}", Name, metricName, value, thisQuery.metricUnit);
                                        RecordMetric(metricName, thisQuery.metricUnit, value);
                                    }
                                }
                            }
                        }
                    }
                    catch (ManagementException e)
                    {
                        logger.Error("Exception occurred in polling. {0}\r\n{1}", e.Message, (string)thisQuery.counterOrQuery);
                    }
                    catch (Exception e)
                    {
                        logger.Error("Exception occurred in processing results. {0}\r\n{1}", e.Message, e.StackTrace);
                    }
                }
                ReportAll();
            }

            private void RecordMetric(string metricName, string metricUnit, double metricValue)
            {
                Metrics.Add(metricName.Replace("/", "."), metricValue);
            }

            private void ReportAll()
            {
            if (output.metrics == null)
            {
                output.metrics = new List<Dictionary<string, object>>();
            }
            if (output.events == null)
            {
                output.events = new List<Dictionary<string, object>>();
            }
            if (output.inventory == null)
            {
                output.inventory = new Dictionary<string, string>();
            }

            output.metrics.Add(Metrics);
                Console.Out.Write(JsonConvert.SerializeObject(output, Formatting.Indented) + "\n");
                Metrics.Clear();
                output.metrics.Clear();
            }
        }
   }
