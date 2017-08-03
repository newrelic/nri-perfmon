
using System;
using System.Collections.Generic;
using System.Management;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;

namespace newrelic_infra_perfmon_plugin
{
    // Config file classes (Config > Counterlist > Counter)

    public class Counter
    {
        public string counter { get; set; }
        public string attrname { get; set; } = PerfmonPlugin.UseOwnName;
    }

    public class Counterlist
    {
        public string provider { get; set; }
        public string category { get; set; }
        public string instance { get; set; }
        public List<Counter> counters { get; set; }
        public string query { get; set; }
        public string eventname { get; set; } = PerfmonPlugin.DefaultEvent;
        public string querytype { get; set; } = PerfmonPlugin.WMIQuery;
        public string querynamespace { get; set; } = PerfmonPlugin.DefaultNamespace;
    }

    public class Config
    {
        public string name { get; set; }
        public List<Counterlist> counterlist { get; set; }
    }

    // Plugin options

    public class Options
    {
        public string ConfigFile { get; set; }
        public int PollingInterval { get; set; }
        public string ComputerName { get; set; }
        public int Verbose { get; set; }
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
        public PerfmonQuery(string query, string ename, string querytype, string querynamespace, List<Counter> members) 
        {
            metricName = ename;
            queryType = querytype;
            counterOrQuery = query;
            queryNamespace = querynamespace ?? PerfmonPlugin.DefaultNamespace;
            if (members != null) {
                foreach(var member in members)
                {
                    queryMembers.Add(member.counter, member.attrname);
                }
            }
        }

        public PerfmonQuery(string pname, string caname, string coname, string iname)
        {
            queryNamespace = PerfmonPlugin.DefaultNamespace;
            queryType = PerfmonPlugin.WMIQuery;
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
        }

        public object counterOrQuery { get; private set; }
        public string metricName { get; private set; }
        public string queryType { get; private set; }
        public Dictionary<string, string> queryMembers { get; private set; } = new Dictionary<string, string>();
        public string queryNamespace { get; private set; }
    }

    public class PerfmonPlugin
    {
        public static string WMIQuery = "wmi_query";
        public static string WMIEvent = "wmi_eventlistener";
        public static string DefaultEvent = "WMIQueryResult";
        public static string DefaultNamespace = "root\\cimv2";
        public static string UseOwnName = "derp";

        string Name { get; set; }
        List<PerfmonQuery> PerfmonQueries { get; set; }
        ManagementScope Scope { get; set; }
        Dictionary<string, Object> Metrics = new Dictionary<string, Object>();
        Output output = new Output();
        int PollingInterval;
        bool IsDebug = false;
        
        public PerfmonPlugin(Options options, Counterlist counter)
        {
            Initialize(options);
            AddCounter(counter, 0);
            if(counter.querytype.Equals(WMIEvent))
            {
                PollingInterval = 0;
            }
        }

        public PerfmonPlugin(Options options, List<Counterlist> counters)
        {
            Initialize(options);
            int whichCounter = -1;
            foreach (Counterlist aCounter in counters)
            {
                whichCounter++;
                AddCounter(aCounter, whichCounter);
            }
        }

        private void Initialize(Options options) {
            PerfmonQueries = new List<PerfmonQuery>();
            Name = options.ComputerName;
            PollingInterval = options.PollingInterval;
            if (options.Verbose > 0)
            {
                IsDebug = true;
            }
            output.name = Name;
            output.protocol_version = "1";
            output.integration_version = "0.1.0";
            output.metrics = new List<Dictionary<string, object>>();
            output.events = new List<Dictionary<string, object>>();
            output.inventory = new Dictionary<string, string>();
        }

        public void AddCounter(Counterlist aCounter, int whichCounter) {
            if (!String.IsNullOrEmpty(aCounter.query))
            {
                if (aCounter.counters != null)
                {
                    foreach (var testCounter in aCounter.counters) {
                        if (String.IsNullOrEmpty(testCounter.counter))
                        {
                            
                            Console.Error.WriteLine("plugin.json contains malformed counter: counterlist[{0}] missing 'counter' in 'counters'. Please review and compare to template.", whichCounter);
                            return;
                        }
                    }
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.query, aCounter.eventname, aCounter.querytype, aCounter.querynamespace, aCounter.counters));
                }
                else
                {
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.query, aCounter.eventname, aCounter.querytype, aCounter.querynamespace, null));
                }
                return;
            }

            if (String.IsNullOrEmpty(aCounter.provider) || String.IsNullOrEmpty(aCounter.category))
            {
                Console.Error.WriteLine("plugin.json contains malformed counter: counterlist[{0}] missing 'provider' or 'category'. Please review and compare to template.", whichCounter);
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
                string countersStr = "";
                foreach (var aSubCounter in aCounter.counters)
                {
                    whichSubCounter++;
                    if (String.IsNullOrEmpty(aSubCounter.counter))
                    {
                        Console.Error.WriteLine("plugin.json contains malformed counter: 'counters' in counterlist[{0}] missing 'counter' in element {1}. Please review and compare to template.", whichCounter, whichSubCounter);
                        continue;
                    } else {
                        if(String.IsNullOrEmpty(countersStr)) {
                            countersStr = aSubCounter.counter;
                        } else {
                            countersStr += (", " + aSubCounter.counter);
                        }
                    }
                }
                if(!String.IsNullOrEmpty(countersStr)) {
                    PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, countersStr, instanceName));
                }
            }
            else
            {
                Console.Error.WriteLine("plugin.json contains malformed counter: counterlist[{0}] missing 'counters'. Please review and compare to template.", whichCounter);
            }
        }

        public void RunThread() {
            TimeSpan pollingIntervalTS = TimeSpan.FromMilliseconds(PollingInterval);
            if (pollingIntervalTS.TotalMilliseconds == 0)
            {
                Debug("Running in constant polling mode.");
                do
                {
                    PollCycle();
                }
                while (1 == 1);
            }
            else
            {
                Debug("Running with polling interval of " + pollingIntervalTS);
                do
                {
                    DateTime then = DateTime.Now;
                    PollCycle();
                    DateTime now = DateTime.Now;
                    TimeSpan elapsedTime = now.Subtract(then);
                    if (pollingIntervalTS.TotalMilliseconds > elapsedTime.TotalMilliseconds)
                    {
                        Thread.Sleep(pollingIntervalTS - elapsedTime);
                    }
                    else
                    {
                        Thread.Sleep(pollingIntervalTS);
                    }
                }
                while (1 == 1);
            }
        }

        public void PollCycle()
        {
            Metrics.Add("event_type", PerfmonPlugin.DefaultEvent);
            var metricNames = new Dictionary<string, int>();

            // Working backwards so we can safely delete queries that fail because of invalid classes.
            for(int i = PerfmonQueries.Count-1; i>=0; i--)
            {
                var thisQuery = PerfmonQueries[i];
                try
                {
                    if (thisQuery.counterOrQuery is PerformanceCounter)
                    {
                        try
                        {
                            Debug("Collecting Perf Counter: " + ((PerformanceCounter)thisQuery.counterOrQuery).ToString());

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
                                Debug(string.Format("{0}/{1}: {2} {3}", Name, metricName, value));
                                Metrics.Add(metricName, value);

                            }
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine("Exception occurred in processing next value. {0}\r\n{1}", e.Message, e.StackTrace);
                        }
                    }
                    else if (thisQuery.counterOrQuery is string)
                    {
                        try
                        {
                            string scopeString = "\\\\" + Name + "\\" + thisQuery.queryNamespace;
                            if (Scope == null)
                            {
                                Debug("Setting up scope: " + scopeString);
                                Scope = new ManagementScope(scopeString);
                            }
                            else if (Scope != null && !Scope.Path.ToString().Equals(scopeString))
                            {
                                Debug("Updating Scope Path from " + Scope.Path + " to " + scopeString);
                                Scope = new ManagementScope(scopeString);
                            }

                            if (!Scope.IsConnected)
                            {
                                Debug("Connecting to scope: " + scopeString);
                                Scope.Connect();
                            }
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine("Unable to connect to \"{0}\". {1}", Name, e.Message);
                            continue;
                        }
                        if (thisQuery.queryType.Equals(WMIQuery))
                        {
                            Debug("Running Query: " + (string)thisQuery.counterOrQuery);
                            var queryResults = (new ManagementObjectSearcher(Scope,
                                new ObjectQuery((string)thisQuery.counterOrQuery))).Get();
                            foreach (ManagementObject result in queryResults)
                            {
                                {
                                    RecordMetricMap(thisQuery, result);
                                    continue;
                                }
                            }
                        } 
                            else if (thisQuery.queryType.Equals(WMIEvent)) 
                        {
                            Debug("Running Event Listener: " + thisQuery.counterOrQuery);
                            var watcher = new ManagementEventWatcher(Scope,
                                new EventQuery((string)thisQuery.counterOrQuery)).WaitForNextEvent();
                            RecordMetricMap(thisQuery, watcher);
                        }

                    }
                }
                catch (ManagementException e)
                {
                    Console.Error.WriteLine("Exception occurred in polling. {0}: {1}", e.Message, (string)thisQuery.counterOrQuery);
                    if(e.Message.ToLower().Contains("invalid class") || e.Message.ToLower().Contains("not supported")) 
                    {
                        Console.Error.WriteLine("Query Removed: {0}", thisQuery.counterOrQuery);
                        PerfmonQueries.RemoveAt(i);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Exception occurred in processing results. {0}\r\n{1}", e.Message, e.StackTrace);
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
                    if (member.Value.Equals(PerfmonPlugin.UseOwnName))
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
                            foreach (var memberprop in memberprops.Properties)
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
            } 
            else
            {
                foreach (PropertyData prop in properties.Properties)
                {
                    propsOut.Add(prop.Name, prop.Value);
                }
            }

            output.metrics.Add(propsOut);
        }

        private void ReportAll()
        {
            if (Metrics.Count > 1)
            {
                output.metrics.Add(Metrics);
            }
            if (output.metrics.Count > 0)
            {
                if(IsDebug)
                {
                    Console.Error.WriteLine("Output: ");
                    Console.Error.Write(JsonConvert.SerializeObject(output, Formatting.Indented) + "\n");
                }
                Console.Out.Write(JsonConvert.SerializeObject(output, Formatting.None) + "\n");
            }
            Metrics.Clear();
            output.metrics.Clear();
        }

        private void Debug(string output)
        {
            if (this.IsDebug)
            {
                Console.Error.WriteLine("Thread-" + Thread.CurrentThread.ManagedThreadId + " : " + output);
            }
        }
    }
}
