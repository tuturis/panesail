#requires -Version 5.1
<#
.SYNOPSIS
Runs the complete Core and Windows component test receipts. Build first.
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$failures = 0
foreach ($testProject in @('WindowPalette.Core.Tests', 'WindowPalette.Windows.Tests')) {
    $framework = if ($testProject -eq 'WindowPalette.Core.Tests') { 'net10.0' } else { 'net10.0-windows10.0.26100.0' }
    $assembly = Join-Path $repositoryRoot "tests\$testProject\bin\x64\$Configuration\$framework\$testProject.dll"
    if (-not (Test-Path -LiteralPath $assembly)) {
        Write-Warning "Build output missing for $testProject. Run scripts\build.ps1 first."
        $failures++
        continue
    }
    Write-Host "Running $testProject"
    & dotnet.exe $assembly
    if ($LASTEXITCODE -ne 0) { $failures++ }
}
if ($failures -gt 0) { exit 1 }
exit 0
