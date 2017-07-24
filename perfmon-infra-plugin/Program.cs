using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using System.IO;

namespace newrelic_infra_perfmon_plugin
{
    class Program
    {
        static void Main(string[] args)
        {
            TimeSpan pollingInterval = new TimeSpan(10000); // In ms
            string configFile = "config.json";
            if (args.Length > 0)
            {
                configFile = args[0];
            }

            StreamReader configFileReader = new StreamReader(configFile);
            Config properties = JsonConvert.DeserializeObject<Config>(configFileReader.ReadToEnd());

            string name = properties.name;
            if (String.IsNullOrEmpty(name) || String.Equals(name, "ComputerName"))
            {
                name = Environment.MachineName;
            }

            List<Counterlist> counterlist = properties.counterlist;
            if (counterlist.Count == 0)
            {
                throw new Exception("'counterlist' is empty. Do you have a 'config/plugin.json' file?");
            }

            PerfmonPlugin plugin = new PerfmonPlugin(name, counterlist);
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
