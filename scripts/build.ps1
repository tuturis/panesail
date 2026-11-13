#requires -Version 5.1
<#
.SYNOPSIS
Restores and builds Window Palette; this does not deploy or register the package.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Auto', 'VisualStudio', 'DotNet')][string]$Toolchain = 'Auto',
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $repositoryRoot 'WindowPalette.sln'

& (Join-Path $PSScriptRoot 'verify-manifest.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Manifest verification failed. Review the full check receipt before changing the source.' }

$dotnet = Get-Command dotnet.exe -ErrorAction Stop
$buildExit = 1
$environmentNames = @('MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $repositoryRoot
try {
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $savedErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $sdkOutput = @(& $dotnet.Source --version 2>&1)
        $sdkExit = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $savedErrorPreference }
    if ($sdkExit -ne 0 -or ($sdkOutput -join '').Trim() -notmatch '^10\.0\.[0-9]+$') {
        throw ('The stable .NET 10 SDK selected by global.json is unavailable. Run scripts\check-prerequisites.ps1. ' + ($sdkOutput -join [Environment]::NewLine))
    }

    $msbuild = $null
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if ($Toolchain -ne 'DotNet' -and (Test-Path -LiteralPath $vswhere)) {
        $candidates = @(& $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
        if ($LASTEXITCODE -eq 0 -and $candidates.Count -gt 0) { $msbuild = $candidates[0] }
    }
    if ($Toolchain -eq 'VisualStudio' -and [string]::IsNullOrWhiteSpace($msbuild)) {
        throw 'Visual Studio 2026 MSBuild was requested but not found. Install the repository .vsconfig prerequisites.'
    }

    $buildArguments = @(
        $solution,
        '/t:Build',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:UseSharedCompilation=false',
        '/nodeReuse:false',
        '/maxCpuCount:1',
        '/verbosity:minimal',
        '/nologo'
    )
    if (-not $NoRestore) { $buildArguments += '/restore' }
    if (-not [string]::IsNullOrWhiteSpace($msbuild)) {
        Write-Host "Building $Configuration/$Platform with $msbuild"
        & $msbuild @buildArguments
    }
    else {
        Write-Host "Building $Configuration/$Platform with .NET SDK MSBuild. Visual Studio package deployment remains a separate step."
        & $dotnet.Source msbuild @buildArguments
    }
    $buildExit = $LASTEXITCODE
    if ($buildExit -eq 0) { Write-Host 'Build succeeded. Deploy the package and reload Command Palette to verify host discovery.' }
}
finally {
    Pop-Location
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
}
exit $buildExit
