using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using System.IO;
using Fclp;

namespace newrelic_infra_perfmon_plugin
{
    class Program
    {
        static void Main(string[] args)
        {
            var pollingIntervalFloor = 10000;
            var defaultCompName = "ThisComputer";
            var defaultConfigFile = "config.json";
            var defaultPrettyPrint = false;

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

            parser.Setup(arg => arg.ComputerName)
            .As('n', "compName")
            .SetDefault(defaultCompName)
            .WithDescription("Name of computer that you want to poll");

            parser.Setup(arg => arg.PrettyPrint)
            .As('p', "prettyPrint")
            .SetDefault(defaultPrettyPrint)
            .WithDescription("Pretty-print JSON output for visual debugging");

            parser.Setup(arg => arg.Debug)
            .As('d', "debug")
            .SetDefault(false)
            .WithDescription("Debug logging mode");

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

            options.ComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? options.ComputerName;
            options.ConfigFile = Environment.GetEnvironmentVariable("CONFIGFILE") ?? options.ConfigFile;
            
            // All of the possibilities for polling interval figured here...
            string env_PollingInterval = Environment.GetEnvironmentVariable("POLLINGINTERVAL");
            int pollingInterval = pollingIntervalFloor;
            if(!String.IsNullOrEmpty(env_PollingInterval)) {
                pollingInterval = Int32.Parse(env_PollingInterval);         
            } else { 
                pollingInterval = options.PollingInterval;
            }
            if (pollingInterval < pollingIntervalFloor) {
                pollingInterval = pollingIntervalFloor;
            }

            options.PollingInterval = pollingInterval;

            List<Counterlist> counterlist = null;
            try
            {
                StreamReader configFileReader = new StreamReader(options.ConfigFile);
                Config properties = JsonConvert.DeserializeObject<Config>(configFileReader.ReadToEnd());
                counterlist = properties.counterlist;
            } catch (IOException)
            {
                Console.Error.WriteLine("ERROR: " + options.ConfigFile + " could not be found or opened.");
                Environment.Exit(1);
            }

            if (String.IsNullOrEmpty(options.ComputerName) || String.Equals(options.ComputerName, defaultCompName))
            {
                options.ComputerName = Environment.MachineName;
            }

            if (counterlist == null || counterlist.Count == 0)
            {
                throw new Exception("'counterlist' is empty. Do you have a 'config/plugin.json' file?");
            }
            List<Counterlist> mainCounters = new List<Counterlist>();
            List<Thread> eventThreads = new List<Thread>();

            foreach (var thisCounter in counterlist)
            {
                if (thisCounter.querytype.Equals(PerfmonPlugin.WMIEvent))
                {
                    PerfmonPlugin aPlugin = new PerfmonPlugin(options, thisCounter);
                    Thread aThread = new Thread(new ThreadStart(aPlugin.RunThread));
                    eventThreads.Add(aThread);
                    aThread.Start();
                }
                else
                {
                    mainCounters.Add(thisCounter);
                }
            }
               
            if (mainCounters.Count > 0)
            {
                // Console.Out.WriteLine("Running main counter list.");
                PerfmonPlugin thisPlugin = new PerfmonPlugin(options, mainCounters);
                thisPlugin.RunThread();
            }

            // If the main function has nothing or exits, wait on other threads (which should stay running)
            foreach(Thread aThread in eventThreads)
            {
                aThread.Join();
            }

        }
    }
}
