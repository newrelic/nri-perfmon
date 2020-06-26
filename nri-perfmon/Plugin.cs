
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace NewRelic
{

    class PerfCounter
    {
        public string category { get; private set; }
        public string instance { get; private set; }
        public List<string> counters { get; private set; }

        public Dictionary<string, PerformanceCounter> PerformanceCounters { get; private set; }

        public PerfCounter(string cname, List<string> cos, string iname)
        {
            PerformanceCounters = new Dictionary<string, PerformanceCounter>();
            category = cname;
            instance = iname;
            counters = cos;
        }

        private string calcHashCode(string uno, string dos, string tres)
        {
            return uno + dos + tres;
        }

        public void PopulatePerformanceCounters()
        {
            var instanceArr = new String[] { instance };
            PerformanceCounterCategory thisCategory = null;
            try
            {
                thisCategory = new PerformanceCounterCategory(category);
                if (String.IsNullOrEmpty(instance))
                {
                    instanceArr = thisCategory.GetInstanceNames();
                }
            }
            catch (InvalidOperationException ioe)
            {
                Log.WriteLog(String.Format("{0}\nSkipping monitoring of PerfCounter {1}", ioe.Message, category), Log.LogLevel.WARN);
                return;
            }

            if (instanceArr.Length == 0)
            {
                PerformanceCounter[] allCounters = null;
                try
                {
                    allCounters = thisCategory.GetCounters();
                }
                catch (ArgumentException)
                {
                    //This should never happen. See 
                    //https://referencesource.microsoft.com/#System/services/monitoring/system/diagnosticts/PerformanceCounterCategory.cs,466
                    //Better be defensive and handle the potential exception, in case .NET changes the implementation, or in case it is implemented
                    //differently in old versions of .NET.
                    Log.WriteLog(String.Format("PerfCounter {0} has no instances, skipping.", category), Log.LogLevel.WARN);
                }

                if (allCounters != null)
                {
                    AddMultipleCounters(allCounters);
                }
                else
                {
                    return;
                }
            }

            foreach (var thisInstance in instanceArr)
            {
                foreach (var counter in counters)
                {
                    if (String.Equals(counter, "*"))
                    {
                        var allCounters = thisCategory.GetCounters(thisInstance);
                        AddMultipleCounters(allCounters,thisInstance);
                    }
                    else
                    {
                        AddSingleCounter(counter, thisInstance);
                    }
                }
            }
        }

        private void AddMultipleCounters(PerformanceCounter[] counters, string thisInstance = "")
        {
            if (counters.Length == 0)
            {
                Log.WriteLog(String.Format("PerfCounter {0}/{1} has no counters, skipping.", category, thisInstance), Log.LogLevel.VERBOSE);
                return;
            }
            foreach (var thisCounter in counters)
            {
                try
                {
                    string pcKey = calcHashCode(thisCounter.CategoryName, thisCounter.CounterName, thisInstance);
                    if (!PerformanceCounters.ContainsKey(pcKey))
                    {
                        var counter = String.IsNullOrEmpty(thisInstance)
                            ? new PerformanceCounter(thisCounter.CategoryName, thisCounter.CounterName, true)
                            : new PerformanceCounter(thisCounter.CategoryName, thisCounter.CounterName, thisInstance, true);
                        PerformanceCounters.Add(pcKey, counter);
                    }
                }

                catch (InvalidOperationException ioe)
                {
                    Log.WriteLog(String.Format("{0}\nSkipping monitoring of PerfCounter {1}/{2}/{3}", ioe.Message, thisCounter.CategoryName, thisCounter.CounterName, thisInstance), Log.LogLevel.WARN);
                    return;
                }
            }
        }

        private void AddSingleCounter(string counter, string thisInstance)
        {
            try
            {

                string pcKey = calcHashCode(category, counter, thisInstance);
                if (!PerformanceCounters.ContainsKey(pcKey))
                {
                    PerformanceCounters.Add(pcKey, new PerformanceCounter(category, counter, thisInstance, true));
                }
            }
            catch (InvalidOperationException ioe)
            {
                Log.WriteLog(String.Format("{0}\nSkipping monitoring of PerfCounter {1}/{2}/{3}", ioe.Message, category, counter, thisInstance), Log.LogLevel.WARN);
                return;
            }
        }
    }

    public class PerfCounterOut
    {
        public string category { get; private set; }
        public string instance { get; private set; }
        public string counter { get; private set; }
        public Object value { get; private set; }

        public PerfCounterOut(string cname, string co, string iname, Object val)
        {
            value = val;
            category = Regex.Replace(cname, @"[^\d\w:_]", "_");
            instance = iname;
            counter = co.Replace(" ", "").Replace('-', '_').Replace("%", "Percent");
            int whereIsPer = counter.IndexOf('/');
            if (whereIsPer > -1)
            {
                if (counter.Length > whereIsPer + 1)
                {
                    String capChar = counter.Substring(whereIsPer + 1, 1).ToUpper();
                    counter = counter.Remove(whereIsPer + 1, 1).Insert(whereIsPer + 1, capChar);
                }
                counter = counter.Remove(whereIsPer, 1).Insert(whereIsPer, "Per");
            }
        }
    }

    class WMIQuery
    {
        public string eventName { get; private set; }
        public string instanceName { get; private set; }
        public string queryNamespace { get; private set; }
        public Dictionary<string, string> membersToRename { get; private set; }
        public string queryString { get; private set; }
        public string queryType { get; private set; }

        public WMIQuery(string qstr, string ename, string qtype, string qns, List<Counter> rmembers)
        {
            eventName = ename;
            queryType = qtype;
            queryString = qstr;
            queryNamespace = qns ?? PerfmonPlugin.DefaultNamespace;
            if (rmembers != null)
            {
                membersToRename = new Dictionary<string, string>();
                foreach (var member in rmembers)
                {
                    membersToRename.Add(member.counter, member.attrname);
                }
            }
        }

        public WMIQuery(string pname, string caname, string coname, string iname)
        {
            queryNamespace = PerfmonPlugin.DefaultNamespace;
            queryType = PerfmonPlugin.WMIQuery;
            try
            {
                eventName = string.Format("{0}", caname);
                if (String.IsNullOrEmpty(iname))
                {
                    queryString = string.Format("Select Name, {0} from Win32_PerfFormattedData_{1}_{2}", coname, pname, caname);
                }
                else
                {
                    queryString = string.Format("Select Name, {0} from Win32_PerfFormattedData_{1}_{2} Where Name Like '{3}'", coname, pname, caname, iname);
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
                queryString = null;
            }
        }
    }

    public class PerfmonPlugin
    {
        public static string WMIQuery = "wmi_query";
        public static string WMIEvent = "wmi_eventlistener";
        public static string DefaultEvent = "WMIQueryResult";
        public static string DefaultNamespace = "root\\cimv2";
        public static string UseCounterName = "using_counter_name";
        public static string EventTypeAttr = "event_type";
        public static string PerfCounterType = "PerfCounter";
        public String fileName = "";

        string Name { get; set; }
        List<WMIQuery> WMIQueries { get; set; }
        List<PerfCounter> PerfCounters { get; set; }
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
            WMIQueries = new List<WMIQuery>();
            PerfCounters = new List<PerfCounter>();
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
                    int whichOfTheseCounters = -1;
                    foreach (var testCounter in aCounter.counters)
                    {
                        whichOfTheseCounters++;
                        if (String.IsNullOrEmpty(testCounter.counter))
                        {
                            Log.WriteLog(String.Format("{0} contains malformed counter: 'counters' in counterlist[{1}] missing 'counter' in element {2}. Please review and compare to template.",
                                fileName, whichCounter, whichOfTheseCounters),
                                Log.LogLevel.ERROR);
                            continue;
                        }
                    }
                }
                WMIQueries.Add(new WMIQuery(aCounter.query, aCounter.eventname, aCounter.querytype, aCounter.querynamespace, aCounter.counters));
            }
            else
            {
                if (String.IsNullOrEmpty(aCounter.provider) || String.IsNullOrEmpty(aCounter.category))
                {
                    Log.WriteLog(String.Format("{0} contains malformed counter: counterlist[{1}] missing 'provider' or 'category'. Please review and compare to template.",
                        fileName, whichCounter), Log.LogLevel.ERROR);
                    return;
                }
                if (aCounter.counters == null)
                {
                    Log.WriteLog(String.Format("{0} contains malformed counter: counterlist[{1}] missing 'counters'. Please review and compare to template.",
                        fileName, whichCounter), Log.LogLevel.ERROR);
                    return;
                }

                string instanceName = string.Empty;
                if (!String.IsNullOrEmpty(aCounter.instance) && !String.Equals(aCounter.instance, "*"))
                {
                    instanceName = aCounter.instance.ToString();
                }

                int whichOfTheseCounters = -1;
                foreach (var testCounter in aCounter.counters)
                {
                    whichOfTheseCounters++;
                    if (String.IsNullOrEmpty(testCounter.counter))
                    {
                        Log.WriteLog(String.Format("{0} contains malformed counter: 'counters' in counterlist[{1}] missing 'counter' in element {2}. Please review and compare to template.",
                            fileName, whichCounter, whichOfTheseCounters), Log.LogLevel.ERROR);
                        continue;
                    }
                }

                if (String.Equals(aCounter.provider, PerfCounterType))
                {
                    List<string> pcounters = new List<string>();
                    foreach (var pCounter in aCounter.counters)
                    {
                        pcounters.Add(pCounter.counter);
                    }
                    PerfCounters.Add(new PerfCounter(aCounter.category, pcounters, instanceName));
                }
                else
                {
                    string countersStr = "";
                    foreach (var wCounter in aCounter.counters)
                    {
                        if (String.IsNullOrEmpty(countersStr))
                        {
                            countersStr = wCounter.counter;
                        }
                        else
                        {
                            countersStr += (", " + wCounter.counter);
                        }
                    }
                    if (!String.IsNullOrEmpty(countersStr))
                    {
                        WMIQueries.Add(new WMIQuery(aCounter.provider, aCounter.category, countersStr, instanceName));
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
                    PollWMIQueries();
                    if (output.metrics.Count > 0)
                    {
                        Log.WriteLog("Metric output", output, Log.LogLevel.CONSOLE);
                        output.metrics.Clear();
                    }
                }
                while (1 == 1);
            }
            else
            {
                Log.WriteLog("Running with polling interval of " + pollingIntervalTS, Log.LogLevel.VERBOSE);
                do
                {
                    DateTime then = DateTime.Now;
                    if (WMIQueries.Count > 0)
                    {
                        PollWMIQueries();
                    }
                    if (PerfCounters.Count > 0)
                    {
                        PollPerfCounters();
                    }
                    if (output.metrics.Count > 0)
                    {
                        Log.WriteLog("Metric output", output, Log.LogLevel.CONSOLE);
                        output.metrics.Clear();
                    }
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

        public void PollWMIQueries()
        {
            // Working backwards so we can safely delete queries that fail because of invalid classes.
            for (int i = WMIQueries.Count - 1; i >= 0; i--)
            {
                var thisQuery = WMIQueries[i];
                if (String.IsNullOrEmpty(thisQuery.queryString))
                {
                    Log.WriteLog(String.Format("Null query removed"), Log.LogLevel.WARN);
                    WMIQueries.RemoveAt(i);
                    continue;
                }
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
                try
                {
                    if (thisQuery.queryType.Equals(WMIQuery))
                    {
                        Log.WriteLog("Running Query: " + (string)thisQuery.queryString, Log.LogLevel.VERBOSE);
                        var queryResults = (new ManagementObjectSearcher(Scope,
                            new ObjectQuery((string)thisQuery.queryString))).Get();
                        if (queryResults.Count == 0)
                        {
                            Log.WriteLog(String.Format("Query \"{0}\" returned no results.", thisQuery.queryString), Log.LogLevel.VERBOSE);
                        }
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
                        Log.WriteLog("Running Event Listener: " + thisQuery.queryString, Log.LogLevel.VERBOSE);
                        var watcher = new ManagementEventWatcher(Scope,
                            new EventQuery((string)thisQuery.queryString)).WaitForNextEvent();
                        RecordMetricMap(thisQuery, watcher);
                    }
                }
                catch (ManagementException e)
                {
                    Log.WriteLog(String.Format("Exception occurred in polling. {0}: {1}", e.Message, (string)thisQuery.queryString), Log.LogLevel.ERROR);
                    if (e.Message.ToLower().Contains("invalid class") || e.Message.ToLower().Contains("not supported"))
                    {
                        Log.WriteLog(String.Format("Query Removed: {0}", thisQuery.queryString), Log.LogLevel.WARN);
                        WMIQueries.RemoveAt(i);
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLog(String.Format("Exception occurred in processing results. {0}\r\n{1}", e.Message, e.StackTrace), Log.LogLevel.ERROR);
                }
            }
        }

        public void PollPerfCounters()
        {
            List<PerfCounterOut> outPCs = new List<PerfCounterOut>();
            for (int i = PerfCounters.Count - 1; i >= 0; i--)
            {
                var thisPC = PerfCounters[i];
                try
                {
                    thisPC.PopulatePerformanceCounters();
                    foreach (var pcInPC in thisPC.PerformanceCounters.Values)
                    {
                        Log.WriteLog(string.Format("Collecting Perf Counter: {0}/{1}", pcInPC.CategoryName, pcInPC.CounterName), Log.LogLevel.VERBOSE);
                        float value = pcInPC.NextValue();
                        if (!float.IsNaN(value))
                        {
                            outPCs.Add(new PerfCounterOut(pcInPC.CategoryName, pcInPC.CounterName, pcInPC.InstanceName, value));
                            Log.WriteLog(string.Format("Perf Counter result: {0}/{1}: {2}", pcInPC.CategoryName, pcInPC.CounterName, value), Log.LogLevel.VERBOSE);
                        }
                        else
                        {
                            Log.WriteLog(string.Format("Perf Counter returned no result: {0}/{1}", pcInPC.CategoryName, pcInPC.CounterName), Log.LogLevel.VERBOSE);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLog(String.Format("Exception occurred in processing next value. {0}\r\n{1}", e.Message, e.StackTrace), Log.LogLevel.ERROR);
                }
            }

            if (outPCs.Count > 0)
            {
                var organizedPerfCounters = from counter in outPCs
                                            group counter by new
                                            {
                                                counter.category,
                                                counter.instance
                                            }
                    into op
                                            select new
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
                        countersOut.Add(item.counter, item.value);
                    }
                    if (countersOut.Count > 2)
                    {
                        output.metrics.Add(countersOut);
                    }
                }
            }
        }

        private void RecordMetricMap(WMIQuery thisQuery, ManagementBaseObject properties)
        {
            Dictionary<string, Object> propsOut = new Dictionary<string, Object>();
            propsOut.Add(EventTypeAttr, thisQuery.eventName);
            if (thisQuery.membersToRename != null && thisQuery.membersToRename.Count > 0)
            {
                foreach (var member in thisQuery.membersToRename)
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
