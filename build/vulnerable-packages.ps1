<#
.SYNOPSIS
  Fails when any project references a package with a High or Critical known vulnerability (09 section 10).
#>
$ErrorActionPreference = 'Stop'
$output = dotnet list MonitorCloud.slnx package --vulnerable --include-transitive 2>&1 | Out-String
Write-Host $output
if ($output -match '\b(High|Critical)\b') {
    Write-Error 'High or Critical vulnerable packages found.'
    exit 1
}
