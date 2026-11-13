# Development

The initial development workflow targets Windows 11 x64 with the .NET 10 SDK selected by the repository, Visual Studio 2026 with the declared workloads, Windows SDK 26100, and PowerToys with Command Palette enabled. Local package deployment requires Windows Developer Mode. [Prerequisite checker](../scripts/check-prerequisites.ps1), [SDK selection](../global.json), [Command Palette extension development](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension)

From the repository root in PowerShell:

```powershell
.\scripts\check-prerequisites.ps1
.\scripts\verify-manifest.ps1
.\scripts\build.ps1 -Configuration Debug -Platform x64
.\scripts\test.ps1 -Configuration Debug
.\scripts\build.ps1 -Configuration Release -Platform x64
```

The test command runs the geometry/policy and Windows component suites. A build or component test does not establish Command Palette discovery, actual drag behavior, or a Store installation. [Test script](../scripts/test.ps1), [validation summary](../VALIDATION.md)

For local development, deploy the extension project from Visual Studio, then reload extensions in Command Palette. Open PaneSail, explicitly enable automatic resizing, and verify the intended behavior on the target displays. The development package is not a production-signed Store package. [Microsoft's extension workflow](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension)

## Preparing a Store candidate

The Store submission uses the identity assigned to the publisher. Obtain the exact package name, publisher, and publisher display name from Partner Center; do not substitute the local development identity. [Store identity guidance](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store)

From the repository root, provide those values when prompted:

```powershell
$packageName = Read-Host 'Partner Center package name'
$publisher = Read-Host 'Partner Center publisher'
$publisherDisplayName = Read-Host 'Partner Center publisher display name'
.\scripts\prepare-store-manifest.ps1 -PackageName $packageName -Publisher $publisher -PublisherDisplayName $publisherDisplayName
$manifest = (Resolve-Path '.local\store\Package.appxmanifest').Path
$projects = @(Get-ChildItem .\src -Filter '*.csproj' -Recurse | Where-Object { Select-String -LiteralPath $_.FullName -SimpleMatch '<EnableMsixTooling>true</EnableMsixTooling>' -Quiet })
if ($projects.Count -ne 1) { throw 'Could not identify exactly one extension project.' }
dotnet msbuild $projects[0].FullName /restore /t:Build /p:Configuration=Release /p:Platform=x64 /p:PaneSailStoreBuild=true "/p:PaneSailStoreManifest=$manifest" "/p:PaneSailStoreIdentityName=$packageName" "/p:PaneSailStorePublisher=$publisher" "/p:PaneSailStorePublisherDisplayName=$publisherDisplayName"
```

This prepares a separate manifest and builds isolated output; it does not submit, sign, deploy, or establish Store approval. The Store-specific build has not yet been verified with a real Partner Center identity. Keep signing material outside the repository. Before publishing, test install, update, uninstall, startup, disabled state, real drag behavior, and the target environment; record unsupported or untested cases accurately. [Store publishing](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store)
