<#
.SYNOPSIS
  Coverage gates of 09 section 2 on a merged Cobertura report:
  Domain >= 90% line, Application >= 85% line and >= 75% branch, whole backend >= 80% line.
.EXAMPLE
  pwsh build/coverage-gate.ps1 -Report TestResults/coverage/Cobertura.xml
#>
param(
    [Parameter(Mandatory)] [string] $Report
)

$ErrorActionPreference = 'Stop'
[xml] $xml = Get-Content -Raw $Report

function Get-Rates([string] $packageName) {
    $package = $xml.coverage.packages.package | Where-Object { $_.name -eq $packageName }
    if (-not $package) { return $null }
    $lines = $package.SelectNodes('.//class/lines/line')
    $covered = ($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $branchCovered = 0; $branchTotal = 0
    foreach ($line in $lines | Where-Object { $_.branch -eq 'True' }) {
        if ($line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
            $branchCovered += [int]$Matches[1]; $branchTotal += [int]$Matches[2]
        }
    }
    [pscustomobject]@{
        Name   = $packageName
        Line   = if ($lines.Count) { [math]::Round(100 * $covered / $lines.Count, 2) } else { 100 }
        Branch = if ($branchTotal) { [math]::Round(100 * $branchCovered / $branchTotal, 2) } else { 100 }
        Lines  = $lines.Count
    }
}

$gates = @(
    @{ Package = 'MonitorCloud.Domain'; Line = 90; Branch = 0 },
    @{ Package = 'MonitorCloud.Application'; Line = 85; Branch = 75 }
)

$failed = $false
foreach ($gate in $gates) {
    $rates = Get-Rates $gate.Package
    if (-not $rates) { Write-Host "$($gate.Package): no coverage data"; continue }
    Write-Host ("{0}: line {1}% branch {2}% ({3} lines)" -f $rates.Name, $rates.Line, $rates.Branch, $rates.Lines)
    if ($rates.Line -lt $gate.Line) { Write-Error "$($gate.Package) line coverage $($rates.Line)% < $($gate.Line)%" -ErrorAction Continue; $failed = $true }
    if ($rates.Branch -lt $gate.Branch) { Write-Error "$($gate.Package) branch coverage $($rates.Branch)% < $($gate.Branch)%" -ErrorAction Continue; $failed = $true }
}

$total = [math]::Round(100 * [double]$xml.coverage.'line-rate', 2)
Write-Host "Backend: line $total%"
if ($total -lt 80) { Write-Error "Backend line coverage $total% < 80%" -ErrorAction Continue; $failed = $true }

if ($failed) { exit 1 }
