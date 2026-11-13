# PaneSail release notes

## Initial release preparation

PaneSail is being prepared for its first public release. The planned release targets Windows 11 x64 and integrates with Command Palette; the Microsoft Store listing is not yet available. [Command Palette extensions](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extensibility-overview)

- Request a resize for eligible windows after dragging them between monitors, with independent screen-proportion and shape-preserving modes. The destination application may constrain or decline the request. [Sizing rules](src/WindowPalette.Core/Geometry.cs), [application sizing](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanging)
- Leave windows unchanged when their released dimensions already fit the destination's usable area. [Fit decision](src/WindowPalette.Windows/WindowResizeOperation.cs)
- Enable or disable automatic resizing and choose whether it starts with Windows. The feature begins disabled. [Feature controls](src/WindowPalette.CmdPal/Pages/DemoPage.cs), [defaults](src/WindowPalette.Core/PaletteSettings.cs)

The initial implementation is conservative: some custom title bars and window-manager gestures may be skipped, and app-specific minimum sizes can prevent an exact result. Separate desktop verification of both modes, mixed-DPI displays, actual sign-in startup, and the Store-installed package has not yet been established. [Eligibility rules](src/WindowPalette.Core/GesturePolicy.cs), [validation summary](VALIDATION.md)
