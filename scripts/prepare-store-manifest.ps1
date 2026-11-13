#requires -Version 5.1
<#
.SYNOPSIS
Creates a separate Store manifest from the development manifest using values from Partner Center.
.DESCRIPTION
This does not build, sign, deploy, register, or publish a package. The existing development
manifest is never modified. Supply actual Partner Center values before producing a real release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$PackageName,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Publisher,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [string]$OutputPath,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$storeRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.local\store'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $storeRoot 'Package.appxmanifest' }
$target = [IO.Path]::GetFullPath($OutputPath)
$storePrefix = $storeRoot.TrimEnd('\') + '\'
if (-not $target.StartsWith($storePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The generated manifest must be inside the repository .local\store directory.'
}
if ($PackageName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$' -or $PackageName -eq 'WindowPalette.650a2c0b926c') {
    throw 'Supply the actual Partner Center package identity name, not the development identity.'
}
if ([string]::IsNullOrWhiteSpace($Publisher) -or $Publisher.Trim() -ne $Publisher -or $Publisher -eq 'CN=WindowPalette.Development') {
    throw 'Supply the exact Partner Center publisher, not the development publisher.'
}
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName) -or $PublisherDisplayName.Trim() -ne $PublisherDisplayName) {
    throw 'Supply the exact Partner Center publisher display name.'
}
if ((Test-Path -LiteralPath $target) -and -not $Force) { throw 'The target exists. Use -Force only to replace the generated manifest intentionally.' }
$source = Join-Path $repositoryRoot 'src\WindowPalette.CmdPal\Package.appxmanifest'
$manifest = New-Object System.Xml.XmlDocument
$manifest.PreserveWhitespace = $true
$manifest.Load($source)
$identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
$identity.SetAttribute('Name', $PackageName)
$identity.SetAttribute('Publisher', $Publisher)
$identity.SetAttribute('Version', '0.3.0.0')
$manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Properties"]/*[local-name()="PublisherDisplayName"]').InnerText = $PublisherDisplayName
$manifest.SelectSingleNode('//*[local-name()="ExeServer"]/*[local-name()="Class"]').SetAttribute('Id', 'C56BE4C0-5ADB-47CB-A9D6-859ED1AD66D1')
$manifest.SelectSingleNode('//*[local-name()="CreateInstance"]').SetAttribute('ClassId', 'C56BE4C0-5ADB-47CB-A9D6-859ED1AD66D1')
$manifest.SelectSingleNode('//*[local-name()="StartupTask"]').SetAttribute('TaskId', 'PaneSail.Background')
$manifest.SelectSingleNode('//*[local-name()="AppExtension"]').SetAttribute('Id', 'PaneSail')
foreach ($family in @($manifest.SelectNodes('//*[local-name()="TargetDeviceFamily"]'))) {
    $family.SetAttribute('MinVersion', '10.0.22000.0')
    $family.SetAttribute('MaxVersionTested', '10.0.26100.0')
}
New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
$manifest.Save($target)
Write-Host "Generated isolated Store manifest: $target"
Write-Host 'Check all values against Partner Center. No package has been built or published.'
