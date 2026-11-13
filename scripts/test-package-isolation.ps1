#requires -Version 7.6
<#
.SYNOPSIS
Checks compiled development and synthetic Store identity isolation without starting workers.
.DESCRIPTION
Run with PowerShell 7.6 or later on .NET 10, after building both configurations. The Store output must be built from a test-only manifest;
these checks do not establish Store account identity, package installation or certification.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\WindowPalette.Windows'
$framework = 'net10.0-windows10.0.26100.0'
$devPath = Join-Path $project "bin\x64\$Configuration\$framework\WindowPalette.Windows.dll"
$storePath = Join-Path $project "bin\store\x64\$Configuration\$framework\WindowPalette.Windows.dll"
if (-not (Test-Path -LiteralPath $devPath) -or -not (Test-Path -LiteralPath $storePath)) { throw 'Build both the development and isolated Store configurations first.' }
$dev = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($devPath))
$store = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($storePath))
$devIdentity = $dev.GetType('WindowPalette.Windows.ProcessIdentity', $true)
$storeIdentity = $store.GetType('WindowPalette.Windows.ProcessIdentity', $true)
$devController = $dev.GetType('WindowPalette.Windows.FeatureController', $true)
$storeController = $store.GetType('WindowPalette.Windows.FeatureController', $true)
$devSettings = $dev.GetType('WindowPalette.Windows.SettingsStore', $true)
$storeSettings = $store.GetType('WindowPalette.Windows.SettingsStore', $true)
$staticInternal = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
$devFallback = $devSettings.GetProperty('FallbackDirectory', $staticInternal).GetValue($null)
$storeFallback = $storeSettings.GetProperty('FallbackDirectory', $staticInternal).GetValue($null)
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$devPipe = $devIdentity.GetProperty('PipeName').GetValue($null)
$storePipe = $storeIdentity.GetProperty('PipeName').GetValue($null)
$devMutex = $devIdentity.GetProperty('WorkerMutex').GetValue($null)
$storeMutex = $storeIdentity.GetProperty('WorkerMutex').GetValue($null)
$devStartup = $devController.GetField('StartupTaskId').GetRawConstantValue()
$storeStartup = $storeController.GetField('StartupTaskId').GetRawConstantValue()
$checks = @(
    [pscustomobject]@{ name = 'different per-package control pipes'; passed = $devPipe -ne $storePipe },
    [pscustomobject]@{ name = 'shared per-user session arbitration mutex'; passed = $devMutex -eq $storeMutex },
    [pscustomobject]@{ name = 'stable development control pipe'; passed = $devPipe.StartsWith('WindowPalette.', [StringComparison]::Ordinal) },
    [pscustomobject]@{ name = 'separate Store control pipe'; passed = $storePipe.StartsWith('PaneSail.', [StringComparison]::Ordinal) },
    [pscustomobject]@{ name = 'different startup task identities'; passed = $devStartup -eq 'WindowPalette.Background' -and $storeStartup -eq 'PaneSail.Background' },
    [pscustomobject]@{ name = 'stable development fallback settings directory'; passed = $devFallback -eq (Join-Path $localAppData 'WindowPalette') },
    [pscustomobject]@{ name = 'separate Store fallback settings directory'; passed = $storeFallback -eq (Join-Path $localAppData 'PaneSail') -and $storeFallback -ne $devFallback }
)
foreach ($check in $checks) { Write-Host ("{0} {1}" -f $(if ($check.passed) { 'PASS' } else { 'FAIL' }), $check.name) }
if (@($checks | Where-Object { -not $_.passed }).Count -gt 0) { exit 1 }
