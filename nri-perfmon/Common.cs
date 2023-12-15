
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace NewRelic
{
    /*public class BaseIdentity {
        public readonly static WindowsIdentity Identity = WindowsIdentity.GetCurrent();
    }*/

    // Config file classes (Config > Counterlist > Counter)

    public class Counter
    {
        public string counter { get; set; }
        public string attrname { get; set; } = PerfmonPlugin.UseCounterName;
        public string parser { get; set; } = "";
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
        public Options() { }

        // For cloning to multiple instances
        public Options(Options that) {
            ConfigFile = that.ConfigFile;
            PollingInterval = that.PollingInterval;
            MachineName = that.MachineName;
            RunOnce = that.RunOnce;
            Verbose = that.Verbose;
            IgnoreInfoLogs = that.IgnoreInfoLogs
            UserName = that.UserName;
            DomainName = that.DomainName;
            Password = that.Password;
        }

        public string ConfigFile { get; set; }
        public int PollingInterval { get; set; }
        public bool RunOnce { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }
        public string DomainName { get; set; }
        public string Password { get; set; }
        public bool Verbose { get; set; }
        public bool IgnoreInfoLogs { get; set; }

        public Options OptionsWithoutPassword()
        {
            Options withoutPassword = new Options(this);
            if (!String.IsNullOrEmpty(withoutPassword.Password)) {
                withoutPassword.Password = "REDACTED";
            }
            return withoutPassword;
        }
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

        public static bool IgnoreInfoLogs = false;

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
            else if (loglevel == LogLevel.INFO) // If ignore info logs is enabled, do not report info logs
            {
                if (IgnoreInfoLogs)
                {
                    return;
                } else
                {
                    ELog.WriteEntry(message, (EventLogEntryType)loglevel);
                }

            } 
            else if (loglevel != LogLevel.INFO) // WARN & ERROR messages written to Event Log (unless Verbose is true)
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

    public static class Util
    {
        // In case we're using Linux or MacOS, don't want to attempt anything with native libraries
        // https://stackoverflow.com/questions/5116977/how-to-check-the-os-version-at-runtime-e-g-on-windows-or-linux-without-using#5117005
        public static bool IsLinuxOrMacOS
        {
            get
            {
                int p = (int)Environment.OSVersion.Platform;
                return (p == 4) || (p == 6) || (p == 128);
            }
        }
    }
}
