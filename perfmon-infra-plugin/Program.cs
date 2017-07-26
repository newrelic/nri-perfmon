using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using System.IO;
using CommandLine;
using CommandLine.Text;

namespace newrelic_infra_perfmon_plugin
{
    class Options
    {
        [Option('c', "configFile", DefaultValue = "config.json", Required = false,
          HelpText = "Config file to use.")]
        public string ConfigFile { get; set; }

        [Option('i', "pollingInterval", DefaultValue = 10000, Required = false,
         HelpText = "Frequency of polling (ms).")]
        public int PollingInterval { get; set; }

        [Option('n', "computerName", DefaultValue = "ThisComputer", Required = false,
         HelpText = "Name of computer that you want to poll.")]
        public string ComputerName { get; set; }

        [Option('p', "prettyPrint", DefaultValue = false, Required = false,
          HelpText = "Pretty-print JSON output for visual debugging.")]
        public bool PrettyPrint { get; set; }

        [ParserState]
        public IParserState LastParserState { get; set; }

        [HelpOption(HelpText = "This help dialog.")]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this,
              (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var options = new Options();
            if (!CommandLine.Parser.Default.ParseArguments(args, options))
            {
                Environment.Exit(1);
            }

            TimeSpan pollingInterval = new TimeSpan(options.PollingInterval);

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

            if (String.IsNullOrEmpty(options.ComputerName) || String.Equals(options.ComputerName, "ThisComputer"))
            {
                options.ComputerName = Environment.MachineName;
            }

            if (counterlist == null || counterlist.Count == 0)
            {
                throw new Exception("'counterlist' is empty. Do you have a 'config/plugin.json' file?");
            }

            PerfmonPlugin plugin = new PerfmonPlugin(options.ComputerName, counterlist, options.PrettyPrint);
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
