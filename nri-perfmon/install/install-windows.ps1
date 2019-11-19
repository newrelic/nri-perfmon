Get-Content "install.config" | ForEach-Object -Begin { $inst_vars = @{} } -Process { $k = [regex]::split($_,'='); if (($k[0].CompareTo("") -ne 0) -and ($k[0].StartsWith("[") -ne $True) -and ($k[0].StartsWith("#") -ne $True)) { $inst_vars.Add($k[0],$k[1]) } }
$integration_name = $inst_vars.Item("INTEGRATION_NAME").trim()
$executable_name = $inst_vars.Item("EXECUTABLE_NAME").trim() + ".exe"

Write-Host "`n ## New Relic $integration_name Installer ## `n"

$definition_dir = "C:\Program Files\New Relic\newrelic-infra\custom-integrations"
$config_dir = "C:\Program Files\New Relic\newrelic-infra\integrations.d"
$script_dir = Split-Path $script:MyInvocation.MyCommand.Path

Write-Host "`n----------------------------"
Write-Host " Admin requirement check...  "
Write-Host "----------------------------`n"

### require admin rights
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]"Administrator")) {
  Write-Warning "This setup needs admin permissions. Please run this file as admin."
  break
}

Write-Host " ...passed!  "

Write-Host "`n----------------------------"
Write-Host " Copying files...  "
Write-Host "----------------------------`n"

if (-not (Test-Path "$definition_dir\$integration_name")) {
  New-Item $definition_dir\$integration_name -ItemType directory
}
Copy-Item -Force -Recurse $script_dir\$executable_name -Destination $definition_dir\$integration_name

if ($inst_vars.ContainsKey("OTHER_FILES")) {
  $inst_vars.Item("OTHER_FILES").split(",") | ForEach-Object {
    $this_file = $_.trim()
    if (Test-Path $script_dir\$this_file -PathType Leaf) {
      Copy-Item -Force $script_dir\$this_file -Destination $definition_dir\$integration_name
    } else {
      Write-Host "$this_file does not exist, skipping."
    }
  }
}

if ($inst_vars.ContainsKey("OS_SPECIFIC_DEFINITIONS") -and $inst_vars.Item("OS_SPECIFIC_DEFINITIONS") -eq 'true') {
  Copy-Item -Force $script_dir\$integration_name-definition-windows.yml -Destination $definition_dir\$integration_name-definition.yml
} else {
  Copy-Item -Force $script_dir\$integration_name-definition.yml -Destination $definition_dir
}

if ($inst_vars.ContainsKey("OS_SPECIFIC_CONFIGS") -and $inst_vars.Item("OS_SPECIFIC_CONFIGS") -eq 'true') {
  Copy-Item -Force $script_dir\$integration_name-config-windows.yml -Destination $config_dir\$integration_name-config.yml
} else {
  Copy-Item -Force $script_dir\$integration_name-config.yml -Destination $config_dir
}

Write-Host " ...finished.  "

Write-Host "`n---------------------------------------"
Write-Host " Creating Event Log Source... "
Write-Host "-----------------------------------------`n"

if(![System.Diagnostics.EventLog]::SourceExists($integration_name)) {
	Write-Host "Creating log event type: $integration_name" 
	[System.Diagnostics.EventLog]::CreateEventSource($integration_name, "Application")
} else {
	Write-Host "Log event type $integration_name already exists" 
}

Write-Host "`n---------------------------------------"
Write-Host " Restarting New Relic Infrastructure agent... "
Write-Host "-----------------------------------------`n"
$serviceName = 'newrelic-infra'
$nrServiceInfo = Get-Service -Name $serviceName

if ($nrServiceInfo.Status -ne 'Running') {
  Write-Host "New Relic Infrastructure not running currently, starting..."
  Start-Service -Name $serviceName
} else {
  Stop-Service -Name $serviceName
  Start-Service -Name $serviceName
  Write-Host " Restart complete! "
}
