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

            parser.SetupHelp("?", "help")
             .Callback(text => Console.WriteLine(text));

            var options = parser.Parse(args);

            if (options.HasErrors)
            {
                parser.HelpOption.ShowHelp(parser.Options);
                Environment.Exit(1);
            } else if (options.HelpCalled)
            {
                Environment.Exit(0);
            }

            parser.Object.ComputerName = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? parser.Object.ComputerName;
            parser.Object.ConfigFile = Environment.GetEnvironmentVariable("CONFIGFILE") ?? parser.Object.ConfigFile;
            
            // All of the possibilities for polling interval figured here...
            string env_PollingInterval = Environment.GetEnvironmentVariable("POLLINGINTERVAL");
            int pollingInterval = pollingIntervalFloor;
            if(!String.IsNullOrEmpty(env_PollingInterval)) {
                pollingInterval = Int32.Parse(env_PollingInterval);         
            } else { 
                pollingInterval = parser.Object.PollingInterval;
            }
            if (pollingInterval < pollingIntervalFloor) {
                pollingInterval = pollingIntervalFloor;
            }
            var pollingIntervalTS = TimeSpan.FromMilliseconds(pollingInterval);

            List<Counterlist> counterlist = null;
            try
            {
                StreamReader configFileReader = new StreamReader(parser.Object.ConfigFile);
                Config properties = JsonConvert.DeserializeObject<Config>(configFileReader.ReadToEnd());
                counterlist = properties.counterlist;
            } catch (IOException)
            {
                Console.Error.WriteLine("ERROR: " + parser.Object.ConfigFile + " could not be found or opened.");
                Environment.Exit(1);
            }

            if (String.IsNullOrEmpty(parser.Object.ComputerName) || String.Equals(parser.Object.ComputerName, defaultCompName))
            {
                parser.Object.ComputerName = Environment.MachineName;
            }

            if (counterlist == null || counterlist.Count == 0)
            {
                throw new Exception("'counterlist' is empty. Do you have a 'config/plugin.json' file?");
            }
            List<Counterlist> mainCounters = new List<Counterlist>();
            List<Thread> asyncThreads = new List<Thread>();

            foreach (var thisCounter in counterlist)
            {
                if (thisCounter.querytype.Equals(PerfmonPlugin.WMIEvent))
                {
                    PerfmonPlugin aPlugin = new PerfmonPlugin(
                        parser.Object.ComputerName, thisCounter, parser.Object.PrettyPrint, new TimeSpan(0));
                    Thread aThread = new Thread(new ThreadStart(aPlugin.RunThread));
                    asyncThreads.Add(aThread);
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
                PerfmonPlugin thisPlugin = new PerfmonPlugin(parser.Object.ComputerName, mainCounters, parser.Object.PrettyPrint, pollingIntervalTS);
                thisPlugin.RunThread();
            }

            // If the main function has nothing or exits, wait on other threads (which should stay running)
            foreach(Thread aThread in asyncThreads)
            {
                aThread.Join();
            }

        }
    }
}
