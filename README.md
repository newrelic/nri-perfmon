[![New Relic Experimental header](https://github.com/newrelic/opensource-website/raw/master/src/images/categories/Experimental.png)](https://opensource.newrelic.com/oss-category/#new-relic-experimental)

# nri-perfmon - Windows Perfmon/WMI On-Host Integration for New Relic Infrastructure

This integration is capable of...
* running WMI queries and collecting their result
* collecting Performance Counters (aka Perfmon Counters)
* running against the local host or a remote host
* reporting the results to New Relic as events, via the Infrastructure Agent

## Links

* **[Download The Latest Release](https://github.com/newrelic/nri-perfmon/releases/latest)**
* **[Library Of Config Files](nri-perfmon/config)** - includes examples for .NET, ASP, BizTalk, Exchange, Hyper-V, MSSQL, Windows Event Logs and more!

## Installation

**NOTE: If you are installing this integration (not building it from scratch), [Download The Latest Release HERE](https://github.com/newrelic/nri-perfmon/releases/latest) instead of cloning this repo!**

### Requirements

* .NET Framework 3.5 or greater (including all 4.x installations)
* New Relic account
* New Relic Infrastructure Agent installed on a Windows server

### Option 1: Installer

1. Unzip nri-perfmon to a temporary location on the host
1. Run Powershell in "Run As Administrator" mode
1. In Powershell, change to the directory where you unzipped nri-perfmon.
1. Run `install-windows.ps1`, which will place the files in their proper locations and restart the Infra Agent.

### Option 2: Manual

1. Unzip nri-perfmon to a temporary location on the host
1. Create `nri-perfmon` under `C:\Program Files\New Relic\newrelic-infra\custom-integrations`.
1. Place the following files in `C:\Program Files\New Relic\newrelic-infra\custom-integrations\nri-perfmon`:
	* `nri-perfmon.exe`
	* `nri-perfmon.exe.config`
	* `config.json`
	* `Newtonsoft.Json.dll`
	* `FluentCommandLineParser.dll`
1. Place `nri-perfmon-definition.yml` in `C:\Program Files\New Relic\newrelic-infra\custom-integrations` (ALONGSIDE but NOT IN the `nri-perfmon` folder)
1. Place `nri-perfmon-config.yml` in `C:\Program Files\New Relic\newrelic-infra\integrations.d\`
1. Restart the Infra Agent

## Usage

Upon installation, nri-perfmon should start reporting to your New Relic account with the default counters defined in `config.json`. The following configurations are to suit advanced usage patterns, i.e. remote polling and custom counters.

### Command-line Arguments

If run at command line without anything, the executable should report JSON results from WMI queries specified in `config.json` to stdout, and any error messages to stderr. This is useful for testing and debugging your counter/query configuration.

* `-c | --configFile [file]`: Config file to use (default: `config.json`)
* `-i | --pollInt [nnn]`: Frequency of polling (ms) (default: 10000ms, ignored if less than 10000ms)
* `-n | --compName [name]`: Name of computer that you want to poll (default: local host)
* `-r | --runOnce [true|false]`: Run this integration once and exit, instead of polling (default: false)
* `-v | --verbose [true|false]`: [Verbose Logging Mode](#verbose-logging-mode) (default: false)

#### Verbose Logging Mode

Verbose Logging Mode is meant for testing your [Counters](#counters) and seeing if and how they will appear in NRDB. With Verbose Logging Mode enabled, the following occurs:
* All log messages are written to stderr
* Metrics are pretty-printed to stderr
* No messages will appear in Event Logs
* No metrics are written to stdout
* **NRDB will not show any data from this Integration when running it in Verbose Logging Mode.**

Because stderr messages arent picked up by the NRI Agent in Windows, it is best to use this mode at command line, like so:
```batch
C:\Program Files\New Relic\newrelic-infra\custom-integrations> nri-perfmon\nri-perfmon.exe -v -c C:\path\to\custom_config.json
```

### Configuration Within Infrastructure Agent

To use any of the [Command-Line Arguments listed above](#command-line-arguments), edit `nri-perfmon-definition.yml` and add them as argument lines, like so:

```yaml
#
# New Relic Infrastructure Perfmon Integration
#
name: com.newrelic.perfmon
description: Perfmon On-Host Integration
protocol_version: 1
os: windows
commands:
  metrics:
    command:
      - .\nri-perfmon\nri-perfmon.exe
      - -i
      - 60000
      - -c
      - custom_config.json
      - -n
      - MyCompName
    prefix: integration/nri-perfmon
    interval: 15
```

Note:
The `interval:` field at the bottom does need to be there with a number, but it does not change the polling interval. To do that, add `-i` and `<interval_(ms)>` as consecutive lines to your `command` arguments.

### Counters

* Out-of-the-box, we have collected a set of Perfmon counters that pertain to .NET applications. 
* We have a library of pre-built config files that you can use, **[found here.](nri-perfmon/config)** Contributions to this library by way of Pull Request are welcome!
* If you would like to collect your own counters, customize the `counterlist` in `config.json`, or create your own config file and use `-c your_config_file.json` at execution.

#### `config.json` Format

```json
{
  "counterlist": [{
      "provider": "provider_name|PerfCounter",
      "category": "category_name",
      "instance": "(optional) instance_name",
      "counters": [{
          "counter": "*|counter_name"
        },
        {
          "counter": "another_counter_name"
        }
      ]
    },
    {
      "eventname": "(optional, default: 'WMIQueryResult') insights_event_name",
      "query": "the_whole_WMI_Query",
      "querynamespace": "(optional, default: 'root//cimv2') query_namespace",
      "querytype": "(optional, default: 'wmi_query') wmi_query|wmi_eventlistener",
      "counters": [{
          "counter": "counter_name|counter_class.counter_name",
          "attrname": "(optional) attribute_name_in_insights_event",
          "parser": "regex_to_parse_attribute_value"
        },
        {
          "counter": "another_counter_name"
        }
      ]
    }
  ]
}
```

#### Simple Queries

_**For a walkthrough of creating a Simple Query, please see [TIPS.md](TIPS.md).**_

* The "`provider`, `category`, (optional) `instance`" form of the counter is for building simple queries, with the following limitations:
  * Uses the default namespace (`root/cimv2`)
  * Limited to Select statements against classes with the name `Win32_PerfFormattedData_{provider}_{category}`
  * No custom names for individual attributes
  * Uses the category name as the Insights event type.
* The `instance` property is optional and should *only* be used if you want to show a specific instance.
  * If left out, all instances will be polled automatically.
  * If there are multiple instances returned by the counter|query, each instance name will appear in the `name` attribute of the event.
* You must have at least one `counter` specified in `counters`. You can use wildcard ('\*') as the value to get all counters for that class.

Example of usage:
```json
{
  "provider": "ASPNET",
  "category": "ASPNETApplications",
  "counters": [
    {
      "counter": "RequestsTotal"
    }
  ]
}
```

#### Performance Counters

If you specify the `provider` as `PerfCounter`, it will retrieve the Windows Performance Counter instead of running a WMI query. This can be useful if WMI is returning "all 0's" in a query or the appropriate Performance Counter is easier to find [Click here for a good how-to on using Performance Monitor.](https://techcommunity.microsoft.com/t5/Ask-The-Performance-Team/Windows-Performance-Monitor-Overview/ba-p/375481)

* No custom names for individual attributes
* Uses the category name as the Insights event type.
* The `instance` property is optional and should *only* be used if you want to show a specific instance.
  * If left out, all instances will be polled automatically.
  * If there are multiple instances returned by the counter|query, each instance name will appear in the `name` attribute of the event.
* You must have at least one `counter` specified in `counters`. You can use wildcard ('\*') as the value to get all counters for that class.

Example of usage:
```json
{
  "provider": "PerfCounter",
  "category": "ASP.NET Apps v4.0.30319",
  "counters": [
    {
      "counter": "Requests Total"
    }
  ]
}
```

#### Complex Queries & Event Listeners

* `eventname` (optional) - The event name you wish to create in Insights with the results of your query.
* `query` - The WMI query to be run.
* `querytype` (optional) - only used to run an event listener instead of a typical WMI Query (set to `wmi_eventlistener`)
	* This listener will operate as a separate thread, so that it doesn't impede other queries from running.
* `querynamespace` (optional) - allows you to access any WMI namespace.
	* If set, this query will operate as a separate thread, so that it doesn't impede other queries that use the default namespace.
	* Any escape characters (`\`) must be doubled (`\\`)
* `eventtype` (optional) - set that query's result events in Insights to anything specified here.
* `counters` (optional) - used to specify counters to extract from the query, and transformations to perform on them.
	* If not performing any transformations, you can specify counters in the query itself (i.e. "`Select Name, Description, DeviceID from Win32_PNPEntity`) and out this section.
	* Multiple `counters` entries *can* be against the same counter (see example below).
		* They will need to write to different attributes, otherwise they will overwrite one another.
	* `counter` - the counter name you want to extract/match. Must be exact.
	* `attrname` (optional) - use to rename the counter.
		* If used, that counter name will be renamed in the Insights event to the value set here.
		* If left out, the attribute in Insights will be named with the original name of that counter.
	* `parser` (optional) - use a regular expression to parse an attribute value.
		* It can only be used on Strings. It will be ignored for all other counter types.
		* Any escape characters (`\`) must be doubled (`\\`)
		* If used, the counter value will be matched against the regular expression provided here.
		* If it doesn't match, the original value of the counter will be returned.
		* If it matches and you used groups, the result will be a concatenized string of all of the groups.
		* If it matches and you didn't use groups, the result will be the portion of the string that matched.

Example of usage:
```json
{
	"eventname": "HyperV_GuestNetworkAdapter",
	"query": "SELECT InstanceId, DHCPEnabled, DNSServers, IPAddresses FROM Msvm_GuestNetworkAdapterConfiguration",
	"querynamespace": "ROOT\\virtualization\\v2",
	"counters": [{
			"counter": "InstanceId",
			"attrname": "Name",
			"parser": "Microsoft:GuestNetwork\\\\([0-9A-F-]+)\\\\[0-9A-F-]+"
		},
		{
			"counter": "InstanceId",
			"attrname": "AdapterId",
			"parser": "Microsoft:GuestNetwork\\\\[0-9A-F-]+\\\\([0-9A-F-]+)"
		},
		{ "counter": "DHCPEnabled" },
		{ "counter": "DNSServers" },
		{
			"counter": "IPAddresses",
			"attrName": "IPAddress",
			"parser": "([0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3})"
		},
		{ "counter": "IPAddresses" }
	]
},
```

Notes:
* To retrieve properties from within a counter object, use the format `counter.property`, i.e. `targetInstance.DeviceID`
* If there are multiple instances returned by the counter|query, each instance name will appear in the `name` attribute of the event.

## Support

New Relic has open-sourced this project. This project is provided AS-IS WITHOUT WARRANTY OR DEDICATED SUPPORT. Issues and contributions should be reported to the project here on GitHub. We encourage you to bring your experiences and questions to the [Explorers Hub](https://discuss.newrelic.com) where our community members collaborate on solutions and new ideas.

## Contributing

We encourage your contributions to improve nri-perfmon! Keep in mind when you submit your pull request, you'll need to sign the CLA via the click-through using CLA-Assistant. You only have to sign the CLA one time per project. If you have any questions, or to execute our corporate CLA, required if your contribution is on behalf of a company, please drop us an email at opensource@newrelic.com.

**A note about vulnerabilities**

As noted in our [security policy](../../security/policy), New Relic is committed to the privacy and security of our customers and their data. We believe that providing coordinated disclosure by security researchers and engaging with the security community are important means to achieve our security goals.

If you believe you have found a security vulnerability in this project or any of New Relic's products or websites, we welcome and greatly appreciate you reporting it to New Relic through [HackerOne](https://hackerone.com/newrelic).

## License

nri-perfmon is licensed under the [Apache 2.0](http://apache.org/licenses/LICENSE-2.0.txt) License.
