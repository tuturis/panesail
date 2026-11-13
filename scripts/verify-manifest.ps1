#requires -Version 5.1
<#
.SYNOPSIS
Checks the extension's source manifests and activation wiring without deploying it.
#>
[CmdletBinding()]
param([switch]$Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$projectRoot = Join-Path $repositoryRoot 'src\WindowPalette.CmdPal'
$checks = New-Object 'System.Collections.Generic.List[object]'

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail })
}

function Read-RequiredFile {
    param([string]$RelativePath)
    try {
        $text = Get-Content -LiteralPath (Join-Path $projectRoot $RelativePath) -Raw
        Add-Check "file:$RelativePath" $true 'Present and readable.'
        return $text
    }
    catch {
        Add-Check "file:$RelativePath" $false $_.Exception.Message
        return $null
    }
}

function Test-Asset {
    param([string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $false }
    $assetPath = Join-Path $projectRoot $RelativePath
    if (Test-Path -LiteralPath $assetPath -PathType Leaf) { return $true }
    # MSIX resolves unqualified manifest asset names to scale-qualified resources.
    $parent = Split-Path $assetPath -Parent
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { return $false }
    $stem = [IO.Path]::GetFileNameWithoutExtension($assetPath)
    $extension = [IO.Path]::GetExtension($assetPath)
    $variants = @(Get-ChildItem -LiteralPath $parent -File | Where-Object {
        $_.Name -match ('^' + [regex]::Escape($stem) + '\.(scale-[0-9]+|targetsize-[0-9]+(?:_[A-Za-z0-9-]+)*)' + [regex]::Escape($extension) + '$')
    })
    return $variants.Count -gt 0
}

$manifestText = Read-RequiredFile 'Package.appxmanifest'
$projectText = Read-RequiredFile 'WindowPalette.CmdPal.csproj'
$extensionText = Read-RequiredFile 'Extension.cs'
$programText = Read-RequiredFile 'Program.cs'
$appManifestText = Read-RequiredFile 'app.manifest'
$launchText = Read-RequiredFile 'Properties\launchSettings.json'
$featureControllerText = Read-RequiredFile '..\WindowPalette.Windows\FeatureController.cs'

if ($manifestText -and $projectText -and $extensionText -and $programText) {
    try {
        [xml]$manifest = $manifestText
        [xml]$project = $projectText
        Add-Check 'xml' $true 'Package and project XML parse.'
        $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
        Add-Check 'package-identity' ($null -ne $identity -and $identity.Name -match '^WindowPalette\.[A-Za-z0-9.\-]+$') 'Package name is owned by Window Palette.'
        Add-Check 'publisher' ($null -ne $identity -and $identity.Publisher -eq 'CN=WindowPalette.Development') 'Development publisher matches the documented local identity.'

        $guidMatches = [regex]::Matches($extensionText, '\[Guid\("(?<id>[0-9A-Fa-f-]{36})"\)\]')
        $extensionClass = [regex]::Match($extensionText, 'class\s+(?<name>\w+)\s*:').Groups['name'].Value
        $classNodes = @($manifest.SelectNodes('//*[local-name()="ExeServer"]/*[local-name()="Class"]'))
        $activationNodes = @($manifest.SelectNodes('//*[local-name()="CmdPalProvider"]/*[local-name()="Activation"]/*[local-name()="CreateInstance"]'))
        $sourceGuid = [guid]::Empty
        $sourceGuidValid = $false
        if ($classNodes.Count -eq 1) {
            foreach ($guidMatch in $guidMatches) {
                $candidate = [guid]::Empty
                if ([guid]::TryParse($guidMatch.Groups['id'].Value, [ref]$candidate) -and $classNodes[0].GetAttribute('Id') -ieq $candidate.ToString()) {
                    $sourceGuid = $candidate
                    $sourceGuidValid = $true
                }
            }
        }
        Add-Check 'source-clsid' ($sourceGuidValid -and $sourceGuid -ne [guid]::Empty -and $sourceGuid -ne [guid]'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF') 'Extension declares a non-placeholder COM GUID.'
        $matchingGuids = $sourceGuidValid -and $classNodes.Count -eq 1 -and $activationNodes.Count -eq 1
        if ($matchingGuids) {
            $matchingGuids = $classNodes[0].GetAttribute('Id') -ieq $sourceGuid.ToString() -and $activationNodes[0].GetAttribute('ClassId') -ieq $sourceGuid.ToString()
        }
        Add-Check 'activation-clsids' $matchingGuids 'Extension GUID, COM server class, and Command Palette activation GUID must agree.'
        $registrationPattern = 'RegisterClass\s*<\s*' + [regex]::Escape($extensionClass) + '\s*,\s*IExtension\s*>'
        Add-Check 'class-registration' (-not [string]::IsNullOrWhiteSpace($extensionClass) -and $programText -match $registrationPattern) 'Program registers the declared extension class as IExtension.'
        Add-Check 'com-startup-argument' ($programText.Contains('-RegisterProcessAsComServer')) 'Program handles COM activation startup.'

        $serverNodes = @($manifest.SelectNodes('//*[local-name()="ExeServer"]'))
        $assemblyNode = $project.SelectSingleNode('//*[local-name()="AssemblyName"]')
        $assemblyName = 'WindowPalette.CmdPal'
        if ($null -ne $assemblyNode) { $assemblyName = $assemblyNode.InnerText }
        $expectedExecutable = $assemblyName + '.exe'
        $serverMatches = $serverNodes.Count -eq 1
        if ($serverMatches) {
            $serverMatches = $serverNodes[0].GetAttribute('Executable') -eq $expectedExecutable -and $serverNodes[0].GetAttribute('Arguments') -eq '-RegisterProcessAsComServer'
        }
        Add-Check 'server-executable' $serverMatches "COM server must activate $expectedExecutable with the registration argument."
        $startupExtensions = @($manifest.SelectNodes('//*[local-name()="Extension" and @Category="windows.startupTask"]'))
        $startupTasks = @($manifest.SelectNodes('//*[local-name()="StartupTask"]'))
        Add-Check 'startup-registration' ($startupExtensions.Count -eq 1 -and $startupTasks.Count -eq 1) 'Exactly one packaged startup task is declared.'
        $startupExecutableMatches = $false
        $startupArgumentsMatch = $false
        $startupEntryPointMatches = $false
        $startupIdentityMatches = $false
        $startupDisabled = $false
        if ($startupExtensions.Count -eq 1) {
            $startupExecutableMatches = $startupExtensions[0].GetAttribute('Executable') -eq $expectedExecutable
            $startupArgumentsMatch = $startupExtensions[0].GetAttribute('Parameters', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/10') -eq '--background'
            $startupEntryPointMatches = $startupExtensions[0].GetAttribute('EntryPoint') -eq 'Windows.FullTrustApplication'
        }
        if ($startupTasks.Count -eq 1) {
            $startupIdentityMatches = $startupTasks[0].GetAttribute('TaskId') -eq 'WindowPalette.Background'
            $startupDisabled = $startupTasks[0].GetAttribute('Enabled') -eq 'false'
        }
        Add-Check 'startup-executable' $startupExecutableMatches 'Startup uses the existing packaged executable.'
        Add-Check 'startup-arguments' $startupArgumentsMatch 'Startup activates the independent background process mode.'
        Add-Check 'startup-entrypoint' $startupEntryPointMatches 'Startup uses the full-trust desktop entry point.'
        Add-Check 'startup-identity' $startupIdentityMatches 'Startup task uses the stable Window Palette task identifier.'
        $startupSourceMatches = $false
        if ($featureControllerText -and $startupTasks.Count -eq 1) {
            $startupSourceIds = [regex]::Matches($featureControllerText, 'StartupTaskId\s*=\s*"(?<id>[^"]+)"')
            $startupSourceMatches = @($startupSourceIds | Where-Object { $startupTasks[0].GetAttribute('TaskId') -eq $_.Groups['id'].Value }).Count -eq 1
        }
        Add-Check 'startup-source-identity' $startupSourceMatches 'The startup adapter and manifest use the same task identifier.'
        Add-Check 'startup-opt-in' $startupDisabled 'The manifest leaves startup disabled until the user enables the feature.'
        Add-Check 'background-startup-dispatch' ($programText.Contains('"--background"') -and $programText -match 'BackgroundProcess\.Run\s*\(') 'Program dispatches the resident process independently of COM activation.'
        $application = $manifest.SelectSingleNode('//*[local-name()="Applications"]/*[local-name()="Application"]')
        $applicationMatches = $null -ne $application -and @($expectedExecutable, '$targetnametoken$.exe') -contains $application.GetAttribute('Executable')
        Add-Check 'application-executable' $applicationMatches 'Application executable agrees with the project or the MSIX target-name token.'
        $appExtensions = @($manifest.SelectNodes('//*[local-name()="AppExtension"]'))
        $registrationMatches = $appExtensions.Count -eq 1
        if ($registrationMatches) {
            $registrationMatches = $appExtensions[0].GetAttribute('Name') -eq 'com.microsoft.commandpalette' -and $appExtensions[0].GetAttribute('Id') -eq 'WindowPalette'
        }
        Add-Check 'command-palette-registration' $registrationMatches 'One app extension uses com.microsoft.commandpalette and the WindowPalette extension ID.'
        Add-Check 'commands-interface' ($null -ne $manifest.SelectSingleNode('//*[local-name()="SupportedInterfaces"]/*[local-name()="Commands"]')) 'The provider advertises command support.'
        Add-Check 'full-trust' ($null -ne $manifest.SelectSingleNode('//*[local-name()="Capability" and @Name="runFullTrust"]')) 'The out-of-process desktop extension retains runFullTrust.'
        Add-Check 'template-placeholders' (-not ($manifestText + $programText + $extensionText + $projectText -match 'TemplateCmdPalExtension|TemplateDisplayName|FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF|CN=Microsoft Corporation')) 'No template names, Microsoft publisher, or placeholder CLSID remain.'

        foreach ($content in $project.SelectNodes('//*[local-name()="Content"]')) {
            $relative = $content.GetAttribute('Include')
            if ($relative -like 'Assets\*' -or $relative -like 'Assets/*') {
                Add-Check "project-asset:$relative" (Test-Path -LiteralPath (Join-Path $projectRoot $relative) -PathType Leaf) 'Explicit project asset exists.'
            }
        }
        $assetReferences = New-Object 'System.Collections.Generic.List[string]'
        foreach ($node in $manifest.SelectNodes('//*[local-name()="Logo"]')) { $assetReferences.Add($node.InnerText) }
        foreach ($node in $manifest.SelectNodes('//*')) {
            foreach ($attribute in $node.Attributes) {
                if ($attribute.Name -match 'Logo$|^Image$') { $assetReferences.Add($attribute.Value) }
            }
        }
        foreach ($relative in @($assetReferences | Select-Object -Unique)) {
            Add-Check "manifest-asset:$relative" (Test-Asset $relative) 'Manifest asset exists directly or as an MSIX scale-qualified resource.'
        }
    }
    catch { Add-Check 'manifest-inspection' $false $_.Exception.Message }
}

if ($appManifestText) {
    try {
        [xml]$appManifest = $appManifestText
        $dpi = $appManifest.SelectSingleNode('//*[local-name()="dpiAwareness"]')
        Add-Check 'dpi-awareness' ($null -ne $dpi -and $dpi.InnerText -eq 'PerMonitorV2') 'Per-monitor-v2 DPI declaration is preserved.'
    }
    catch { Add-Check 'app-manifest-xml' $false $_.Exception.Message }
}
if ($launchText) {
    try {
        $launch = $launchText | ConvertFrom-Json
        $packageProfiles = @($launch.profiles.PSObject.Properties | Where-Object { $_.Value.commandName -eq 'MsixPackage' })
        Add-Check 'package-launch-profile' ($packageProfiles.Count -eq 1 -and $packageProfiles[0].Value.doNotLaunchApp -eq $true) 'A packaged deployment profile is retained without launching the COM host directly.'
    }
    catch { Add-Check 'launch-profile-json' $false $_.Exception.Message }
}
foreach ($platform in @('x64', 'arm64')) {
    $relative = "Properties\PublishProfiles\win-$platform.pubxml"
    $profileText = Read-RequiredFile $relative
    if ($profileText) {
        try {
            [xml]$profile = $profileText
            $runtime = $profile.SelectSingleNode('//*[local-name()="RuntimeIdentifier"]')
            Add-Check "publish-runtime:$platform" ($null -ne $runtime -and $runtime.InnerText -eq "win-$platform") 'Publishing profile retains its intended architecture.'
        }
        catch { Add-Check "publish-profile-xml:$platform" $false $_.Exception.Message }
    }
}

$failures = @($checks | Where-Object { -not $_.passed })
$result = [pscustomobject]@{ passed = $failures.Count -eq 0; checks = @($checks.ToArray()) }
if ($Json) { $result | ConvertTo-Json -Depth 5 }
else {
    $checks | Format-Table @{Label='Result'; Expression={if ($_.passed) {'PASS'} else {'FAIL'}}}, name, detail -AutoSize -Wrap
    Write-Host ("Manifest checks: {0} passed, {1} failed." -f ($checks.Count - $failures.Count), $failures.Count)
}
if ($failures.Count -gt 0) { exit 1 }
exit 0
