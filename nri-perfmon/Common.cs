
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

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

    // Logging

    public static class Log
    {
        private static EventLog ELog = new EventLog("Application")
        {
            Source = "nri-perfmon"
        };
        public static bool Verbose = false;

        public static void WriteLog(string message, LogLevel loglevel)
        {
            if (Verbose) // If Verbose is enabled, then it all goes to Stderr
            {
                Console.Error.WriteLine("Thread-" + (object)Thread.CurrentThread.ManagedThreadId + " : " + message);
            }
            else if (loglevel == LogLevel.VERBOSE) // If Verbose is enabled, all verbose messages will already be reported
            {
                return;
            }
            else if (loglevel == LogLevel.CONSOLE) // CONSOLE messages written to Stdout (unless Verbose is true)
            {
                Console.Out.WriteLine(message);
            }
            else // INFO, WARN & ERROR messages written to Event Log (unless Verbose is true)
            {
                ELog.WriteEntry(message, (EventLogEntryType)loglevel);
            }
        }

        public static void WriteLog(string message, object toSerialize, LogLevel loglevel)
        {
            if (loglevel == LogLevel.CONSOLE && !Verbose)
            {
                WriteLog(JsonConvert.SerializeObject(toSerialize, Formatting.None), loglevel);
            }
            else
            {
                WriteLog(message + ":\n" + JsonConvert.SerializeObject(toSerialize, Formatting.Indented), loglevel);
            }
        }

        public enum LogLevel
        {
            ERROR = 1,
            WARN = 2,
            INFO = 4,
            VERBOSE = 8,
            CONSOLE = 16
        }
    }
}
