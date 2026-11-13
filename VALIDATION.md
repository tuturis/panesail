# PaneSail validation summary

This summary is for people deciding whether the initial PaneSail release fits their setup. It distinguishes automated checks of the development implementation from validation of a Store-distributed release.

The source includes automated checks for screen geometry, fitting windows, movement eligibility, settings storage, and the background component's control lifecycle. [Geometry and policy checks](tests/WindowPalette.Core.Tests/Program.cs), [Windows component checks](tests/WindowPalette.Windows.Tests/Program.cs)

Release-candidate installation, Store-signed install and upgrade, actual Windows sign-in, mixed-DPI displays, Snap/FancyZones coexistence, and separate manual runs in both modes remain release checks rather than established results. ARM64 runtime support has not been established. [Feature eligibility](src/WindowPalette.Core/GesturePolicy.cs), [package registration](src/WindowPalette.CmdPal/Package.appxmanifest)
