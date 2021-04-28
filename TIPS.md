# Tips for finding/building new simple entries for `counterlist`

First, to get a list of all counter categories:

```powershell
PS C:\> Get-CimClass Win32_PerfFormattedData* | Select CimClassName
```

Let's take `root/cimv2:Win32_PerfFormattedData_MSSQLSQLEXPRESS_MSSQLSQLEXPRESSBufferManager` for example.

* provider = "MSSQLSQLEXPRESS"
* category = "MSSQLSQLEXPRESSBufferManager"

The format is `Win32_PerfFormattedData_{provider}_{category}`.

Get a list of all counters for that category:

```powershell
PS C:\> Get-CimInstance "Win32_PerfFormattedData_MSSQLSQLEXPRESS_MSSQLSQLEXPRESSBufferManager"

Caption               :
Description           :
Name                  :
Frequency_Object      :
Frequency_PerfTime    :
Frequency_Sys100NS    :
Timestamp_Object      :
Timestamp_PerfTime    :
Timestamp_Sys100NS    :
AWElookupmapsPersec   : 0
AWEstolenmapsPersec   : 0
AWEunmapcallsPersec   : 0
AWEunmappagesPersec   : 0
AWEwritemapsPersec    : 0
Buffercachehitratio   : 100
CheckpointpagesPersec : 0
Databasepages         : 247
FreeliststallsPersec  : 0
Freepages             : 396
LazywritesPersec      : 0
Pagelifeexpectancy    : 251325
PagelookupsPersec     : 56
PagereadsPersec       : 0
PagewritesPersec      : 0
ReadaheadpagesPersec  : 0
Reservedpages         : 0
Stolenpages           : 893
Targetpages           : 84612
Totalpages            : 1536
PSComputerName        :
```

* counter = "Buffercachehitratio"

Putting that all together, you would add the following under `counterlist`:

```json
{
	"provider": "MSSQLSQLEXPRESS",
	"category": "MSSQLSQLEXPRESSBufferManager",
	"counters": [{
		"counter": "Buffercachehitratio"
	}]
}
```

Optionally, you can include an `instance` property. You can see the following in the template.

```json
{
	"provider": "PerfOS",
	"category": "Processor",
	"instance": "_Total",
	"counters": [{
		"counter": "PercentProcessorTime"
	}]
}
```
There is an instance of the counter for each logical processor. The __total_ instance represents the sum of all of them.

If you run this, you'll see all of the instances and the `Name` property is the identifier.
```powershell
Get-CimInstance "Win32_PerfFormattedData_PerfOS_Processor"
```