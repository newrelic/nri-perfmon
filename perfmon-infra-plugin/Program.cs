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
    class Options
    {
        public string ConfigFile { get; set; }
        public int PollingInterval { get; set; }
        public string ComputerName { get; set; }
        public bool PrettyPrint { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // var options = new Options();

            // create a generic parser for the ApplicationArguments type
            var parser = new FluentCommandLineParser<Options>();

            parser.Setup(arg => arg.ConfigFile)
            .As('c', "configFile")
            .SetDefault("config.json")
            .WithDescription("Config file to use");

            parser.Setup(arg => arg.PollingInterval)
            .As('i', "pollInt")
            .SetDefault(10000)
            .WithDescription("Frequency of polling (ms)");

            parser.Setup(arg => arg.ComputerName)
            .As('n', "compName")
            .SetDefault("ThisComputer")
            .WithDescription("Name of computer that you want to poll");

            parser.Setup(arg => arg.PrettyPrint)
            .As('p', "prettyPrint")
            .SetDefault(false)
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

            TimeSpan pollingInterval = TimeSpan.FromMilliseconds(parser.Object.PollingInterval);

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

            if (String.IsNullOrEmpty(parser.Object.ComputerName) || String.Equals(parser.Object.ComputerName, "ThisComputer"))
            {
                parser.Object.ComputerName = Environment.MachineName;
            }

            if (counterlist == null || counterlist.Count == 0)
            {
                throw new Exception("'counterlist' is empty. Do you have a 'config/plugin.json' file?");
            }

            PerfmonPlugin plugin = new PerfmonPlugin(parser.Object.ComputerName, counterlist, parser.Object.PrettyPrint);
            do
            {
                DateTime then = DateTime.Now;
                plugin.PollCycle();
                DateTime now = DateTime.Now;
                TimeSpan elapsedTime = now.Subtract(then);
                if(pollingInterval.TotalMilliseconds > elapsedTime.TotalMilliseconds) {
                    Thread.Sleep(pollingInterval - elapsedTime);
                } else {
                    Thread.Sleep(pollingInterval);
                }
            }
            while(1 == 1);
        }
    }
}
