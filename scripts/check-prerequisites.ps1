#requires -Version 5.1
<#
.SYNOPSIS
Reports build and local deployment prerequisites without installing or changing them.
.DESCRIPTION
Use -Json for a machine-readable receipt. A failed inspection is unknown, not missing.
-BuildOnly requires .NET SDK selection and Windows SDK 26100; it does not certify MSIX deployment.
#>
[CmdletBinding()]
param([switch]$Json, [switch]$BuildOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$checks = New-Object 'System.Collections.Generic.List[object]'

function Add-Check {
    param([string]$Name, [string]$Status, [string]$Detail, [string]$Action = '', [bool]$Required = $true)
    $checks.Add([pscustomobject]@{ name = $Name; status = $Status; required = $Required; detail = $Detail; action = $Action })
}

function Test-NotFoundError {
    param([System.Management.Automation.ErrorRecord]$Record)
    return $Record.CategoryInfo.Category -eq [System.Management.Automation.ErrorCategory]::ObjectNotFound
}

$global = $null
try {
    $global = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
    Add-Check 'sdk-pin' 'passed' ("global.json requests {0} with {1}." -f $global.sdk.version, $global.sdk.rollForward)
}
catch { Add-Check 'sdk-pin' 'unknown' $_.Exception.Message 'Restore a valid global.json before building.' }

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    Add-Check 'dotnet-sdk' 'missing' 'dotnet.exe was not found on PATH.' 'Install the SDK pinned in global.json and open a new terminal.'
}
else {
    Push-Location $repositoryRoot
    try {
        # Windows PowerShell represents redirected native stderr as ErrorRecords.
        # Read the exit code rather than treating SDK-not-found diagnostics as an inspection failure.
        $savedErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $selected = @(& $dotnet.Source --version 2>&1)
            $sdkExit = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $savedErrorPreference }
        $selectedText = ($selected | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        if ($sdkExit -eq 0 -and $selectedText.Trim() -match '^10\.0\.[0-9]+$') {
            Add-Check 'dotnet-sdk' 'passed' ("Selected .NET SDK {0} using global.json." -f $selectedText.Trim())
        }
        elseif ($sdkExit -eq 0) {
            Add-Check 'dotnet-sdk' 'missing' ("The resolved SDK is not stable .NET 10: {0}" -f $selectedText.Trim()) 'Install the stable SDK pinned in global.json.'
        }
        else {
            Add-Check 'dotnet-sdk' 'missing' $selectedText.Trim() 'Install the SDK pinned in global.json (or a compatible patch within its feature band).'
        }
    }
    catch { Add-Check 'dotnet-sdk' 'unknown' $_.Exception.Message 'Run dotnet --version from the repository root in an unrestricted terminal.' }
    finally { Pop-Location }
}

$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsInstallations = @()
try {
    $null = Get-Item -LiteralPath $vswherePath
    $vsJson = (& $vswherePath -products '*' -version '[18.0,19.0)' -format json -utf8) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw 'vswhere failed to inspect Visual Studio installations.' }
    $parsedInstallations = ConvertFrom-Json -InputObject $vsJson
    $vsInstallations = @($parsedInstallations | ForEach-Object { $_ })
    if ($vsInstallations.Count -gt 0) {
        Add-Check 'visual-studio' 'passed' (($vsInstallations | ForEach-Object { '{0} ({1})' -f $_.displayName, $_.installationVersion }) -join '; ') '' (-not $BuildOnly)
    }
    else { Add-Check 'visual-studio' 'missing' 'No Visual Studio 2026 installation was reported.' 'Install Visual Studio 2026 alongside existing Visual Studio versions for the supported IDE deployment workflow.' (-not $BuildOnly) }

    $vsConfig = Get-Content -LiteralPath (Join-Path $repositoryRoot '.vsconfig') -Raw | ConvertFrom-Json
    foreach ($component in $vsConfig.components) {
        $matchingPaths = @(& $vswherePath -products '*' -version '[18.0,19.0)' -requires $component -property installationPath -utf8 | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) { throw "vswhere failed while checking $component." }
        if ($matchingPaths.Count -gt 0) { Add-Check "component:$component" 'passed' ($matchingPaths -join '; ') '' (-not $BuildOnly) }
        else { Add-Check "component:$component" 'missing' 'No compatible Visual Studio installation reports this component.' 'Import .vsconfig in Visual Studio Installer and install the listed component.' (-not $BuildOnly) }
    }
}
catch {
    $status = 'unknown'
    if (Test-NotFoundError $_) { $status = 'missing' }
    Add-Check 'visual-studio-inspection' $status $_.Exception.Message 'Install or inspect Visual Studio 2026 and the repository .vsconfig workloads.' (-not $BuildOnly)
}

$sdkIncludeRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
try {
    $sdkVersions = @(Get-ChildItem -LiteralPath $sdkIncludeRoot -Directory | Where-Object { $_.Name -match '^10\.0\.26100\.[0-9]+$' })
    if ($sdkVersions.Count -gt 0) { Add-Check 'windows-sdk' 'passed' (($sdkVersions | Select-Object -ExpandProperty Name) -join ', ') }
    else { Add-Check 'windows-sdk' 'missing' 'Windows SDK 26100 headers were not found in the standard SDK location.' 'Install Windows 11 SDK 26100 through Visual Studio Installer; CsWinRT needs it for CLI compilation too.' }
}
catch {
    $status = 'unknown'
    if (Test-NotFoundError $_) { $status = 'missing' }
    Add-Check 'windows-sdk' $status $_.Exception.Message 'Inspect or install Windows 11 SDK 26100 through Visual Studio Installer; CsWinRT needs it for CLI compilation too.'
}

if (-not $BuildOnly) {
    try {
        $mode = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
        $modeProperty = $mode.PSObject.Properties['AllowDevelopmentWithoutDevLicense']
        if ($null -ne $modeProperty -and $modeProperty.Value -eq 1) { Add-Check 'developer-mode' 'passed' 'Windows Developer Mode is enabled.' }
        else { Add-Check 'developer-mode' 'missing' 'Windows Developer Mode is disabled.' 'Enable Developer Mode in Windows Settings before local package deployment.' }
    }
    catch {
        $status = 'unknown'
        $detail = $_.Exception.Message
        if (Test-NotFoundError $_) { $status = 'missing'; $detail = 'The Developer Mode opt-in registry setting is absent.' }
        Add-Check 'developer-mode' $status $detail 'Inspect Developer Mode in Windows Settings; enable it for local package deployment.'
    }

    $powerToysPaths = @(
        (Join-Path $env:LOCALAPPDATA 'PowerToys\PowerToys.exe'),
        (Join-Path $env:ProgramFiles 'PowerToys\PowerToys.exe')
    )
    $powerToysFound = @()
    $powerToysErrors = @()
    foreach ($candidate in $powerToysPaths) {
        try { $powerToysFound += Get-Item -LiteralPath $candidate }
        catch { if (-not (Test-NotFoundError $_)) { $powerToysErrors += $_.Exception.Message } }
    }
    try {
        foreach ($uninstallRoot in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
            try {
                foreach ($key in Get-ChildItem -LiteralPath $uninstallRoot) {
                    $entry = Get-ItemProperty -LiteralPath $key.PSPath
                    $display = $entry.PSObject.Properties['DisplayName']
                    $location = $entry.PSObject.Properties['InstallLocation']
                    if ($null -ne $display -and $display.Value -match '^PowerToys' -and $null -ne $location -and -not [string]::IsNullOrWhiteSpace($location.Value)) {
                        $candidate = Join-Path $location.Value 'PowerToys.exe'
                        try { $powerToysFound += Get-Item -LiteralPath $candidate }
                        catch { if (-not (Test-NotFoundError $_)) { $powerToysErrors += $_.Exception.Message } }
                    }
                }
            }
            catch { if (-not (Test-NotFoundError $_)) { $powerToysErrors += $_.Exception.Message } }
        }
    }
    catch { $powerToysErrors += $_.Exception.Message }
    if ($powerToysFound.Count -gt 0) {
        Add-Check 'powertoys' 'passed' (($powerToysFound | Select-Object -Unique FullName | ForEach-Object { $_.FullName }) -join '; ')
    }
    elseif ($powerToysErrors.Count -gt 0) {
        Add-Check 'powertoys' 'unknown' ($powerToysErrors -join '; ') 'Inspect the installed PowerToys application from an unrestricted terminal.'
    }
    else {
        Add-Check 'powertoys' 'missing' 'PowerToys was not found in standard executable or registered install locations.' 'Install the current stable PowerToys release, then enable Command Palette.'
    }

    $appxCommand = Get-Command Get-AppxPackage -ErrorAction SilentlyContinue
    if ($null -eq $appxCommand) {
        Add-Check 'command-palette-package' 'unknown' 'Get-AppxPackage is unavailable in this PowerShell session.' 'Run this check in Windows PowerShell 5.1 or verify Command Palette manually.'
    }
    else {
        try {
            $packages = @(Get-AppxPackage -Name '*CommandPalette*' -ErrorAction Stop)
            if ($packages.Count -gt 0) {
                Add-Check 'command-palette-package' 'passed' (($packages | ForEach-Object { '{0} {1}' -f $_.Name, $_.Version }) -join '; ')
            }
            else { Add-Check 'command-palette-package' 'missing' 'No Command Palette package is registered for this user.' 'Launch PowerToys, enable Command Palette, and open it once.' }
        }
        catch { Add-Check 'command-palette-package' 'unknown' $_.Exception.Message 'Inspect package registration from an unrestricted user terminal; this is not evidence that PowerToys is absent.' }
    }
}

$notReady = @($checks | Where-Object { $_.required -and $_.status -ne 'passed' })
$result = [pscustomobject]@{ ready = $notReady.Count -eq 0; scope = $(if ($BuildOnly) { 'compilation' } else { 'build-and-local-deployment' }); checks = @($checks.ToArray()) }
if ($Json) { $result | ConvertTo-Json -Depth 5 }
else {
    $checks | Format-Table status, required, name, detail, action -AutoSize -Wrap
    Write-Host ("Prerequisites: {0} required check(s) not ready. This does not verify a deployed extension." -f $notReady.Count)
}
if ($notReady.Count -gt 0) { exit 1 }
exit 0
