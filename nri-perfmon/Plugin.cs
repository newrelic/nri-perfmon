
using System;
using System.Collections.Generic;
using System.Management;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using Newtonsoft.Json;

namespace NewRelic
{
    // Config file classes (Config > Counterlist > Counter)

    public class Counter
    {
        public string counter { get; set; }
        public string attrname { get; set; } = PerfmonPlugin.UseCounterName;
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
        public bool Verbose { get; set; }
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
            if (members != null)
            {
                foreach (var member in members)
                {
                    queryMembers.Add(member.counter, member.attrname);
                }
            }
        }

        public PerfmonQuery(string pname, string caname, string coname, string iname)
        {
            queryNamespace = PerfmonPlugin.DefaultNamespace;
            queryType = PerfmonPlugin.WMIQuery;
            try
            {
                if (String.Equals(pname, "PerfCounter"))
                {
                    if (String.IsNullOrEmpty(iname))
                    {
                        metricName = string.Format("{0}", coname);
                        counterOrQuery = new PerformanceCounter(caname, coname);
                    }
                    else
                    {
                        instanceName = iname;
                        metricName = string.Format("{0}", coname);
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
            catch (InvalidOperationException ioe)
            {
                if (String.IsNullOrEmpty(instanceName))
                {
                    Log.WriteLog(ioe.Message + "\nSkipping monitoring of " + caname + "/" + coname, Log.LogLevel.WARN);
                }
                else
                {
                    Log.WriteLog("For instance " + instanceName + ", " + ioe.Message + "\nSkipping monitoring of " + caname + "/" + coname, Log.LogLevel.WARN);
                }
                counterOrQuery = null;
            }
        }

        public object counterOrQuery { get; private set; }
        public string metricName { get; private set; }
        public string instanceName { get; private set; }
        public string queryType { get; private set; }
        public Dictionary<string, string> queryMembers { get; private set; } = new Dictionary<string, string>();
        public string queryNamespace { get; private set; }
    }

    public class PerfCounter
    {
        public string category;
        public string instance;
        public string counter;
        public Object value;
    }

    public class PerfmonPlugin
    {
        public static string WMIQuery = "wmi_query";
        public static string WMIEvent = "wmi_eventlistener";
        public static string DefaultEvent = "WMIQueryResult";
        public static string DefaultNamespace = "root\\cimv2";
        public static string UseCounterName = "using_counter_name";
        public static string EventTypeAttr = "event_type";
        public String fileName = "";

        string Name { get; set; }
        List<PerfmonQuery> PerfmonQueries { get; set; }
        ManagementScope Scope { get; set; }
        Output output = new Output();
        int PollingInterval;

        public PerfmonPlugin(Options options, Counterlist counter)
        {
            Initialize(options);
            fileName = options.ConfigFile;
            AddCounter(counter, 0);
            if (counter.querytype.Equals(WMIEvent))
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

        private void Initialize(Options options)
        {
            PerfmonQueries = new List<PerfmonQuery>();
            Name = options.ComputerName;
            PollingInterval = options.PollingInterval;
            output.name = Name;
            output.protocol_version = "1";
            output.integration_version = "0.1.0";
            output.metrics = new List<Dictionary<string, object>>();
            output.events = new List<Dictionary<string, object>>();
            output.inventory = new Dictionary<string, string>();
        }

        public void AddCounter(Counterlist aCounter, int whichCounter)
        {
            if (!String.IsNullOrEmpty(aCounter.query))
            {
                if (aCounter.counters != null)
                {
                    foreach (var testCounter in aCounter.counters)
                    {
                        if (String.IsNullOrEmpty(testCounter.counter))
                        {
                            Log.WriteLog(String.Format("{0} contains malformed counter: counterlist[{1}] missing 'counter' in 'counters'. Please review and compare to template.", fileName, whichCounter), Log.LogLevel.ERROR);
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
                Log.WriteLog(String.Format("{0} contains malformed counter: counterlist[{1}] missing 'provider' or 'category'. Please review and compare to template.", fileName, whichCounter), Log.LogLevel.ERROR);
                return;
            }

            string instanceName = string.Empty;
            if (!String.IsNullOrEmpty(aCounter.instance) && !String.Equals(aCounter.instance, "*"))
            {
                instanceName = aCounter.instance.ToString();
            }

            if (aCounter.counters != null)
            {
                if (String.Equals(aCounter.provider, "PerfCounter"))
                {
                    AddPerfCounters(whichCounter, aCounter.provider, aCounter.category, aCounter.counters, instanceName);
                }
                else
                {
                    int whichSubCounter = -1;
                    string countersStr = "";
                    foreach (var aSubCounter in aCounter.counters)
                    {
                        whichSubCounter++;
                        if (String.IsNullOrEmpty(aSubCounter.counter))
                        {
                            Log.WriteLog(String.Format("{0} contains malformed counter: 'counters' in counterlist[{1}] missing 'counter' in element {2}. Please review and compare to template.", fileName, whichCounter, whichSubCounter),
                                Log.LogLevel.ERROR);
                            continue;
                        }
                        else
                        {
                            if (String.IsNullOrEmpty(countersStr))
                            {
                                countersStr = aSubCounter.counter;
                            }
                            else
                            {
                                countersStr += (", " + aSubCounter.counter);
                            }
                        }
                    }
                    if (!String.IsNullOrEmpty(countersStr))
                    {
                        PerfmonQueries.Add(new PerfmonQuery(aCounter.provider, aCounter.category, countersStr, instanceName));
                    }
                }
            }
            else
            {
                Log.WriteLog(String.Format("{0} contains malformed counter: counterlist[{1}] missing 'counters'. Please review and compare to template.", fileName, whichCounter), Log.LogLevel.ERROR);
            }
        }

        public void AddPerfCounters(int whichCounter, String aProvider, String aCategory, List<Counter> aCounters, String aInstance)
        {
            var instanceArr = new String[] { aInstance };
            PerformanceCounterCategory thisCategory = null;
            try
            {
                thisCategory = new PerformanceCounterCategory(aCategory);
                if (String.IsNullOrEmpty(aInstance))
                {
                    instanceArr = thisCategory.GetInstanceNames();
                }
            }
            catch (InvalidOperationException ioe)
            {
                Log.WriteLog(String.Format("{0}\nSkipping monitoring of {1}/{2}", ioe.Message, aProvider, aCategory), Log.LogLevel.WARN);
                return;
            }

            foreach (var thisInstance in instanceArr)
            {
                int whichSubCounter = -1;
                foreach (var aCounter in aCounters)
                {
                    whichSubCounter++;
                    var aSubCounter = aCounter.counter;
                    if (String.IsNullOrEmpty(aSubCounter))
                    {
                        Log.WriteLog(String.Format("{0} contains malformed counter: 'counters' in counterlist[{1}] missing 'counter' in element {2}. Please review and compare to template.", fileName, whichCounter, whichSubCounter),
                            Log.LogLevel.ERROR);
                        continue;
                    }
                    if (String.Equals(aSubCounter, "*"))
                    {
                        var allCounters = thisCategory.GetCounters(thisInstance);
                        foreach (var thisCounter in allCounters)
                        {
                            PerfmonQueries.Add(new PerfmonQuery(aProvider, aCategory, thisCounter.CounterName, thisInstance));
                        }
                        break;
                    }
                    else
                    {
                        PerfmonQueries.Add(new PerfmonQuery(aProvider, aCategory, aSubCounter, thisInstance));
                    }
                }
            }
        }

        public void RunThread()
        {
            TimeSpan pollingIntervalTS = TimeSpan.FromMilliseconds(PollingInterval);
            if (pollingIntervalTS.TotalMilliseconds == 0)
            {
                Log.WriteLog("Running in listener mode (no polling interval).", Log.LogLevel.VERBOSE);
                do
                {
                    PollCycle();
                }
                while (1 == 1);
            }
            else
            {
                Log.WriteLog("Running with polling interval of " + pollingIntervalTS, Log.LogLevel.VERBOSE);
                do
                {
                    DateTime then = DateTime.Now;
                    PollCycle();
                    DateTime now = DateTime.Now;
                    TimeSpan elapsedTime = now.Subtract(then);
                    Log.WriteLog("Polling time: " + elapsedTime.ToString(), Log.LogLevel.VERBOSE);
                    if (pollingIntervalTS.TotalMilliseconds > elapsedTime.TotalMilliseconds)
                    {
                        TimeSpan sleepTime = pollingIntervalTS - elapsedTime;
                        Log.WriteLog("Sleeping for: " + sleepTime.ToString(), Log.LogLevel.VERBOSE);
                        Thread.Sleep(sleepTime);
                    }
                    else
                    {
                        Log.WriteLog("Sleeping for: " + pollingIntervalTS.ToString(), Log.LogLevel.VERBOSE);
                        Thread.Sleep(pollingIntervalTS);
                    }
                }
                while (1 == 1);
            }
        }

        public void PollCycle()
        {
            var perfCounterList = new List<PerfCounter>();

            // Working backwards so we can safely delete queries that fail because of invalid classes.
            for (int i = PerfmonQueries.Count - 1; i >= 0; i--)
            {
                var thisQuery = PerfmonQueries[i];
                try
                {
                    if (thisQuery.counterOrQuery is null)
                    {
                        continue;
                    }

                    if (thisQuery.counterOrQuery is PerformanceCounter)
                    {
                        try
                        {
                            var thisPerfCounter = (PerformanceCounter)thisQuery.counterOrQuery;
                            Log.WriteLog("Collecting Perf Counter: " + thisPerfCounter.ToString(), Log.LogLevel.VERBOSE);
                            var perfCounterOut = new PerfCounter();

                            perfCounterOut.category = thisPerfCounter.CategoryName.Replace(' ', '_').Replace('.', '_');
                            perfCounterOut.instance = thisPerfCounter.InstanceName;
                            float value = thisPerfCounter.NextValue();
                            string metricName = thisQuery.metricName;
                            if (!float.IsNaN(value))
                            {
                                Log.WriteLog(string.Format("Perf Counter result: {0}/{1}: {2}", Name, metricName, value), Log.LogLevel.VERBOSE);
                                perfCounterOut.counter = metricName;
                                perfCounterOut.value = value;
                                perfCounterList.Add(perfCounterOut);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.WriteLog(String.Format("Exception occurred in processing next value. {0}\r\n{1}", e.Message, e.StackTrace), Log.LogLevel.ERROR);
                        }
                    }
                    else if (thisQuery.counterOrQuery is string)
                    {
                        try
                        {
                            string scopeString = "\\\\" + Name + "\\" + thisQuery.queryNamespace;
                            if (Scope == null)
                            {
                                Log.WriteLog("Setting up scope: " + scopeString, Log.LogLevel.VERBOSE);
                                Scope = new ManagementScope(scopeString);
                            }
                            else if (Scope != null && !Scope.Path.ToString().Equals(scopeString))
                            {
                                Log.WriteLog("Updating Scope Path from " + Scope.Path + " to " + scopeString, Log.LogLevel.VERBOSE);
                                Scope = new ManagementScope(scopeString);
                            }

                            if (!Scope.IsConnected)
                            {
                                Log.WriteLog("Connecting to scope: " + scopeString, Log.LogLevel.VERBOSE);
                                Scope.Connect();
                            }
                        }
                        catch (Exception e)
                        {
                            Log.WriteLog(String.Format("Unable to connect to \"{0}\". {1}", Name, e.Message), Log.LogLevel.ERROR);
                            continue;
                        }
                        if (thisQuery.queryType.Equals(WMIQuery))
                        {
                            Log.WriteLog("Running Query: " + (string)thisQuery.counterOrQuery, Log.LogLevel.VERBOSE);
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
                            Log.WriteLog("Running Event Listener: " + thisQuery.counterOrQuery, Log.LogLevel.VERBOSE);
                            var watcher = new ManagementEventWatcher(Scope,
                                new EventQuery((string)thisQuery.counterOrQuery)).WaitForNextEvent();
                            RecordMetricMap(thisQuery, watcher);
                        }

                    }
                }
                catch (ManagementException e)
                {
                    Log.WriteLog(String.Format("Exception occurred in polling. {0}: {1}", e.Message, (string)thisQuery.counterOrQuery), Log.LogLevel.ERROR);
                    if (e.Message.ToLower().Contains("invalid class") || e.Message.ToLower().Contains("not supported"))
                    {
                        Log.WriteLog(String.Format("Query Removed: {0}", thisQuery.counterOrQuery), Log.LogLevel.WARN);
                        PerfmonQueries.RemoveAt(i);
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLog(String.Format("Exception occurred in processing results. {0}\r\n{1}", e.Message, e.StackTrace), Log.LogLevel.ERROR);
                }
            }

            if(perfCounterList.Count > 0)
            {
                var organizedPerfCounters = from counter in perfCounterList group counter by new
                    {
                        counter.category,
                        counter.instance
                    }
                    into op select new
                    {
                        Model = op.Key,
                        Data = op
                    };

                foreach (var grouping in organizedPerfCounters)
                {
                    var countersOut = new Dictionary<string, Object>();
                    countersOut.Add(EventTypeAttr, grouping.Model.category);
                    countersOut.Add("name", grouping.Model.instance);
                    foreach (var item in grouping.Data)
                    {
                        countersOut.Add(item.counter.Replace(" ", ""), item.value);
                    }
                    if(countersOut.Count > 2)
                    {
                        output.metrics.Add(countersOut);
                    }
                }
            }

            if (output.metrics.Count > 0)
            {
                Log.WriteLog("Metric output", output, Log.LogLevel.CONSOLE);
                output.metrics.Clear();
            }
        }

        private void RecordMetricMap(PerfmonQuery thisQuery, ManagementBaseObject properties)
        {
            Dictionary<string, Object> propsOut = new Dictionary<string, Object>();
            propsOut.Add(EventTypeAttr, thisQuery.metricName);
            if (thisQuery.queryMembers.Count > 0)
            {
                foreach (var member in thisQuery.queryMembers)
                {
                    string label;
                    if (member.Value.Equals(PerfmonPlugin.UseCounterName))
                    {
                        label = member.Key;
                    }
                    else
                    {
                        label = member.Value;
                    }

                    var splitmem = member.Key.Trim().Split('.');
                    if (properties[splitmem[0]] is ManagementBaseObject)
                    {
                        var memberProps = ((ManagementBaseObject)properties[splitmem[0]]);
                        if (splitmem.Length == 2)
                        {
                            GetValueParsed(propsOut, label, memberProps.Properties[splitmem[1]]);
                        }
                        else
                        {
                            foreach (var memberProp in memberProps.Properties)
                            {
                                GetValueParsed(propsOut, memberProp.Name, memberProp);
                            }
                        }
                    }
                    else
                    {
                        GetValueParsed(propsOut, label, properties.Properties[member.Key]);
                    }
                }
            }
            else
            {
                foreach (PropertyData prop in properties.Properties)
                {
                    GetValueParsed(propsOut, prop.Name, prop);
                }
            }

            output.metrics.Add(propsOut);
        }        

        private void GetValueParsed(Dictionary<string, Object> propsOut, String propName, PropertyData prop)
        {
            if (prop.Value != null)
            {
                Log.WriteLog(String.Format("Parsing: {0}, propValue: {1}, of CimType: {2}", propName, prop.Value.ToString(), prop.Type.ToString()), Log.LogLevel.VERBOSE);
                switch (prop.Type)
                {
                    case CimType.Boolean:
                        propsOut.Add(propName, prop.Value.ToString());
                        break;
                    case CimType.DateTime:
                        try
                        {
                            DateTime dateToParse = DateTime.ParseExact(prop.Value.ToString(), "yyyyMMddHHmmss.ffffff-000", System.Globalization.CultureInfo.InvariantCulture);
                            propsOut.Add(propName, (long)(dateToParse - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
                        }
                        catch (FormatException fe)
                        {
                            Log.WriteLog(String.Format("Could not parse: {0}, propValue: {1}, of CimType: {2}", propName, prop.Value.ToString(), prop.Type.ToString()), Log.LogLevel.VERBOSE);
                            Log.WriteLog("Parsing Exception: " + fe.ToString(), Log.LogLevel.VERBOSE);
                        }
                        break;
                    case CimType.String:
                        if (((String)prop.Value).Length > 0)
                        {
                            propsOut.Add(propName, prop.Value);
                        }
                        break;
                    default:
                        propsOut.Add(propName, prop.Value);
                        break;
                }
            }
        }
    }
}
