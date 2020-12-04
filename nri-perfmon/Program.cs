using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using System.IO;
using Fclp;

namespace NewRelic
{
    class Program
    {
        static char[] SEPARATOR = new char[] {','};
        static void Main(string[] args)
        {
            var pollingIntervalFloor = 10000;
            var defaultCompName = Environment.MachineName;
            var defaultConfigFilePath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            var defaultConfigFile = defaultConfigFilePath + "\\config.json";

            // create a generic parser for the ApplicationArguments type
            var parser = new FluentCommandLineParser<Options>();

            parser.Setup(arg => arg.ConfigFile)
            .As('c', "configFile")
            .SetDefault(defaultConfigFile)
            .WithDescription("Config file to use");

            parser.Setup(arg => arg.PollingInterval)
            .As('i', "pollInt")
            .SetDefault(pollingIntervalFloor)
            .WithDescription("Frequency of polling (ms)");

            parser.Setup(arg => arg.MachineName)
            .As('n', "compName")
            .SetDefault(defaultCompName)
            .WithDescription("Name of computer that you want to poll");

            parser.Setup(arg => arg.UserName)
            .As('u', "userName")
            .SetDefault(Environment.UserName)
            .WithDescription("User to perform polling");

            parser.Setup(arg => arg.DomainName)
            .As('d', "domainName")
            .SetDefault(Environment.UserDomainName)
            .WithDescription("Domain of user to perform polling");

            parser.Setup(arg => arg.Password)
            .As('p', "password")
            .SetDefault("")
            .WithDescription("Password of user to perform polling");

            parser.Setup(arg => arg.RunOnce)
            .As('r', "runOnce")
            .SetDefault(false)
            .WithDescription("Set to true to run this integration once and exit (instead of polling)");

            parser.Setup(arg => arg.Verbose)
            .As('v', "verbose")
            .SetDefault(false)
            .WithDescription("Verbose logging & pretty-print (for testing purposes)");

            parser.SetupHelp("?", "help")
             .Callback(text => Console.WriteLine(text));

            var parse = parser.Parse(args);

            if (parse.HasErrors)
            {
                parser.HelpOption.ShowHelp(parser.Options);
                Environment.Exit(1);
            } else if (parse.HelpCalled)
            {
                Environment.Exit(0);
            }

            var options = parser.Object;
            Log.Verbose = options.Verbose;

            // All of the possibilities for polling interval figured here...
            int pollingInterval = pollingIntervalFloor;
            if (!options.RunOnce)
            {
                string env_PollingInterval = Environment.GetEnvironmentVariable("POLLINGINTERVAL");
                if (String.IsNullOrEmpty(env_PollingInterval) || !int.TryParse(env_PollingInterval, out pollingInterval))
                {
                    pollingInterval = options.PollingInterval;
                }
                if (pollingInterval < pollingIntervalFloor)
                {
                    pollingInterval = pollingIntervalFloor;
                }
                options.PollingInterval = pollingInterval;
            }

            Log.WriteLog("nri-perfmon starting with options", (object)options, Log.LogLevel.INFO);

            List<Counterlist> counterlist = null;
            try
            {
                StreamReader configFileReader = new StreamReader(options.ConfigFile);
                Config properties = JsonConvert.DeserializeObject<Config>(configFileReader.ReadToEnd());
                counterlist = properties.counterlist;
                if (counterlist == null || counterlist.Count == 0)
                {
                    Log.WriteLog("'counterlist' is empty. Please verify " + options.ConfigFile + " is in the expected format (see README).", Log.LogLevel.ERROR);
                    Environment.Exit(1);
                }
            }
            catch (IOException ioe)
            {
                Log.WriteLog(String.Format("{0} could not be found or opened.\n {1}", options.ConfigFile, ioe.Message), Log.LogLevel.ERROR);
                Environment.Exit(1);
            }
            catch (JsonReaderException jre)
            {
                Log.WriteLog(String.Format("{0} could not be parsed.\n {1}", options.ConfigFile, jre.Message), Log.LogLevel.ERROR);
                Environment.Exit(1);
            }


            List<Counterlist> mainCounters = new List<Counterlist>();
            List<Thread> eventThreads = new List<Thread>();

            var splitNames = options.MachineName.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
            foreach (var thisName in splitNames)
            {
                var thisOptions = new Options(options)
                {
                    MachineName = thisName.Trim()
                };

                foreach (var thisCounter in counterlist)
                {
                    // WMI Event Listeners and WMI Queries with special namespaces get their own thread
                    if (thisCounter.querytype.Equals(PerfmonPlugin.WMIEvent) || !thisCounter.querynamespace.Equals(PerfmonPlugin.DefaultNamespace))
                    {
                        PerfmonPlugin aPlugin = new PerfmonPlugin(thisOptions, thisCounter);
                        Thread aThread = new Thread(new ThreadStart(aPlugin.RunThread));
                        eventThreads.Add(aThread);
                        aThread.Start();
                    }
                    else
                    {
                        mainCounters.Add(thisCounter);
                    }
                }
            }

            if (mainCounters.Count > 0)
            {
                Log.WriteLog("nri-perfmon counters", (object)mainCounters, Log.LogLevel.VERBOSE);
                PerfmonPlugin thisPlugin = new PerfmonPlugin(options, mainCounters);
                thisPlugin.RunThread();
            }

            // If the main function has nothing or exits, wait on other threads (which should stay running)
            foreach (Thread aThread in eventThreads)
            {
                aThread.Join();
            }
        }
    }
}
