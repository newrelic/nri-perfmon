
using System;
using System.Collections.Generic;
using System.Management;
using System.Diagnostics;
using System.Threading;
using NLog;
using Newtonsoft.Json;

namespace newrelic_infra_perfmon_plugin
{
    // Config file classes (Config > Counterlist > Counter)

    public class Counter
    {
        public string counter { get; set; }
        public string unit { get; set; } = PerfmonPlugin.DefaultUnit;
    }

    public class Counterlist
    {
        public string provider { get; set; }
        public string category { get; set; }
        public string instance { get; set; }
        public string counter { get; set; }
        public string unit { get; set; } = PerfmonPlugin.DefaultUnit;
        public List<Counter> counters { get; set; }
        public string query { get; set; }
        public string eventname { get; set; } = PerfmonPlugin.DefaultEvent;
        public string querytype { get; set; } = PerfmonPlugin.WMIQuery;
    }

    public class Config
    {
        public string name { get; set; }
        public List<Counterlist> counterlist { get; set; }
    }

    // Plugin options

    class Options
    {
        public string ConfigFile { get; set; }
        public int PollingInterval { get; set; }
        public string ComputerName { get; set; }
        public bool PrettyPrint { get; set; }
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
        public PerfmonQuery(string query, string ename, string querytype)
        {
            metricName = ename;
            unitOrType = querytype;
            counterOrQuery = query;
        }

        public PerfmonQuery(string query, string ename, string querytype, List<Counter> members) 
        {
            metricName = ename;
            unitOrType = querytype;
            counterOrQuery = query;
            foreach(var member in members)
            {
                queryMembers.Add(member.counter, member.unit);
            }
        }

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
            unitOrType = munit;
        }

        public object counterOrQuery { get; private set; }
        public string metricName { get; private set; }
        public string unitOrType { get; private set; } = PerfmonPlugin.DefaultUnit;
        public Dictionary<string, string> queryMembers { get; private set; } = new Dictionary<string, string>();
    }

    public class PerfmonPlugin
    {
        public static string WMIQuery = "wmi_query";
        public static string WMIEvent = "wmi_eventlistener";
        public static string DefaultUnit = "count";
        public static string DefaultEvent = "WMIMetrics";

        private static Logger logger = LogManager.GetCurrentClassLogger();
        string Name { get; set; }
        Formatting PrintFormat { get; set; }
        List<PerfmonQuery> PerfmonQueries { get; set; }
        ManagementScope Scope { get; set; }
        private Dictionary<string, Object> Metrics = new Dictionary<string, Object>();
        private Output output = new Output();
        private TimeSpan PollingInterval;

        public PerfmonPlugin(string compname, Counterlist counter, bool prettyprint, TimeSpan pollingInterval)
        {
            PollingInterval = pollingInterval;
            Initialize(compname, prettyprint);
            AddCounter(counter, 0);
        }

        public PerfmonPlugin(string compname, List<Counterlist> counters, bool prettyprint, TimeSpan pollingInterval)
        {
            PollingInterval = pollingInterval;
            Initialize(compname, prettyprint);
            int whichCounter = -1;
            foreach (Counterlist aCounter in counters)
            {
                whichCounter++;
                AddCounter(aCounter, whichCounter);
            }
        }
        
        private void Initialize(string compname, bool prettyprint) {   
            PerfmonQueries = new List<PerfmonQuery>();
            Name = compname;
            if (prettyprint)
            {
                Console.WriteLine("Pretty Print enabled.");
                PrintFormat = Formatting.Indented;
            } else
            {
                PrintFormat = Formatting.None;
            }
            output.name = Name;
            output.protocol_version = "1";
            output.integration_version = "0.1.0";
            output.metrics = new List<Dictionary<string, object>>();
            output.events = new List<Dictionary<string, object>>();
            output.inventory = new Dictionary<string, string>();
            
            Scope = new ManagementScope("\\\\" + Name + "\\root\\cimv2");
        }

        public void AddCounter(Counterlist aCounter, int whichCounter) {
            if (!String.IsNullOrEmpty(aCounter.query))
            {
                if (aCounter.counters != null)
                {
                    foreach (var testCounter in aCounter.counters) {
                        if (String.IsNullOrEmpty(testCounter.counter))
                        {
                            logger.Error("plugin.json contains malformed counter: counterlist[{0}] missing 'counter' in 'counters'. Please review and compare to template.", whichCounter);
                            return;
                        }
                    }
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.query, aCounter.eventname, aCounter.querytype, aCounter.counters));
                }
                else
                {
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.query, aCounter.eventname, aCounter.querytype));
                }
                return;
            }

            if (String.IsNullOrEmpty(aCounter.provider) || String.IsNullOrEmpty(aCounter.category))
            {
                logger.Error("plugin.json contains malformed counter: counterlist[{0}] missing 'provider' or 'category'. Please review and compare to template.", whichCounter);
                return;
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
                    string metricUnit = aSubCounter.unit;
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, aSubCounter.counter, instanceName, metricUnit));
                }
            }
            else if (!String.IsNullOrEmpty(aCounter.counter))
            {
                string metricUnit = aCounter.unit;
                PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, aCounter.counter, instanceName, metricUnit));
            }
            else
            {
                logger.Error("plugin.json contains malformed counter: counterlist[{0}] missing 'counter' or 'counters'. Please review and compare to template.", whichCounter);
            }
        }

        public void RunThread() {
            if (PollingInterval.TotalMilliseconds == 0)
            {
                // Console.Out.WriteLine("Running this thread: " + Thread.CurrentThread.ManagedThreadId 
                //      + " in constant polling mode.");
                do
                {
                    PollCycle();
                }
                while (1 == 1);
            }
            else
            {
                // Console.Out.WriteLine("Running this thread: " + Thread.CurrentThread.ManagedThreadId
                //      + " with polling interval of " + PollingInterval);
                do
                {
                    DateTime then = DateTime.Now;
                    PollCycle();
                    DateTime now = DateTime.Now;
                    TimeSpan elapsedTime = now.Subtract(then);
                    if (PollingInterval.TotalMilliseconds > elapsedTime.TotalMilliseconds)
                    {
                        Thread.Sleep(PollingInterval - elapsedTime);
                    }
                    else
                    {
                        Thread.Sleep(PollingInterval);
                    }
                }
                while (1 == 1);
            }
        }

        public void PollCycle()
        {
            Metrics.Add("event_type", PerfmonPlugin.DefaultEvent);
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
                                logger.Debug("{0}/{1}: {2} {3}", Name, metricName, value, thisQuery.unitOrType);
                                RecordMetric(metricName, thisQuery.unitOrType, value);
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
                        if (thisQuery.unitOrType.Equals(WMIEvent)) {
                            // Console.Out.WriteLine("Event Listener: " + thisQuery.counterOrQuery);
                            var watcher = new ManagementEventWatcher(Scope,
                                new EventQuery((string)thisQuery.counterOrQuery)).WaitForNextEvent();
                            RecordMetricMap(thisQuery, watcher);
                        }
                        else
                        {
                            var queryResults = (new ManagementObjectSearcher(Scope, new ObjectQuery((string)thisQuery.counterOrQuery))).Get();
                            foreach (ManagementObject result in queryResults)
                            {
                                if (thisQuery.unitOrType.Equals(WMIQuery))
                                {
                                    RecordMetricMap(thisQuery, result);
                                    continue;
                                }

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
                                        logger.Debug("{0}/{1}: {2} {3}", Name, metricName, value, thisQuery.unitOrType);
                                        RecordMetric(metricName, thisQuery.unitOrType, value);
                                    }
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

        private void RecordMetricMap(PerfmonQuery thisQuery, ManagementBaseObject properties)
        {
            Dictionary<string, Object> propsOut = new Dictionary<string, Object>();
            propsOut.Add("event_type", thisQuery.metricName);
            if (thisQuery.queryMembers.Count > 0)
            {
                foreach (var member in thisQuery.queryMembers)
                {
                    string label;
                    if(member.Value.Equals(PerfmonPlugin.DefaultUnit))
                    {
                        label = member.Key;
                    } else
                    {
                        label = member.Value;
                    }

                    var splitmem = member.Key.Trim().Split('.');
                    if (properties[splitmem[0]] is ManagementBaseObject)
                    {
                        var memberprops = ((ManagementBaseObject)properties[splitmem[0]]);
                        if (splitmem.Length == 2)
                        {
                            propsOut.Add(label,
                                memberprops.Properties[splitmem[1]].Value);
                        } else
                        {
                            foreach(var memberprop in memberprops.Properties)
                            {
                                propsOut.Add(memberprop.Name, memberprop.Value);
                            }
                        }
                    }
                    else
                    {
                        propsOut.Add(label, properties.Properties[member.Key].Value);
                    }
                }
            } else
            {
                foreach (PropertyData prop in properties.Properties)
                {
                    propsOut.Add(prop.Name, prop.Value);
                }
            }

            output.metrics.Add(propsOut);
        }

        private void RecordMetric(string metricName, string metricUnit, double metricValue)
        {
            Metrics.Add(metricName.Replace("/", "."), metricValue);
        }

        private void ReportAll()
        {
            if(Metrics.Count > 1)
            {
                output.metrics.Add(Metrics);
            }
            if (output.metrics.Count > 0)
            {
                Console.Out.Write(JsonConvert.SerializeObject(output, PrintFormat) + "\n");
            }
            Metrics.Clear();
            output.metrics.Clear();
        }
    }
}
