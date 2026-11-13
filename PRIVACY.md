# PaneSail privacy notice

**Draft for the source repository. A private privacy contact will be added before the app is distributed.**

This notice describes the PaneSail application, based on the source prepared for its initial release. Windows, PowerToys, the Microsoft Store, and other applications have their own data practices. PaneSail is an independent project.

## What the app accesses and why

When you enable automatic resizing, a background component listens to Windows mouse, keyboard, and window movement events so it can recognize a drag, allow Shift-based window-management gestures, and detect Escape cancellation. Windows delivers keyboard event information to this component; the application's callback examines Shift and Escape for these purposes and does not implement typed-text recording. [Input handling](src/WindowPalette.Windows/WindowEventMonitor.cs), [Windows keyboard hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)

For a candidate drag, the app uses the mouse position, identifiers that match a window to its application, window bounds, display layout, work areas, and scaling information to decide whether and how to resize the window. This information is used locally during the operation. [Window and display queries](src/WindowPalette.Windows/Native/WindowSnapshot.cs), [drag handling](src/WindowPalette.Windows/WindowEventMonitor.cs)

The app saves your enabled choice, resize mode, and Windows startup preference in its local application data. Its extension and background component also exchange local status and control messages through a channel restricted to the current Windows user. [Settings storage](src/WindowPalette.Windows/SettingsStore.cs), [local control](src/WindowPalette.Windows/WorkerConnection.cs)

## Sharing and diagnostics

The reviewed application source does not contain an app-owned analytics service or code that sends drag, keyboard, window, or display information to the maintainers. This statement describes PaneSail's own source; it does not describe data practices of Windows, PowerToys, the Microsoft Store, payment providers, or other applications. [Background component](src/WindowPalette.Windows/BackgroundProcess.cs), [drag handling](src/WindowPalette.Windows/WindowEventMonitor.cs)

The app can emit local diagnostic trace messages for failures or skipped operations. Error details can contain technical information; access to diagnostic output depends on the Windows environment and any tools the user runs. The reviewed source does not configure an application-owned remote diagnostic service. [Diagnostic handling](src/WindowPalette.Windows/FeatureController.cs), [background diagnostics](src/WindowPalette.Windows/BackgroundProcess.cs)

## Your controls and retention

Automatic resizing starts off. You can turn it off in PaneSail to stop its background monitoring, and you can turn off **Start with Windows** independently. Windows also provides its own startup controls. Saved settings remain in local application data between uses. [Defaults](src/WindowPalette.Core/PaletteSettings.cs), [controls](src/WindowPalette.Windows/FeatureController.cs)

Windows normally removes a packaged app's local data when the app is uninstalled; operating-system backup, restore, and uninstall choices can affect retained copies. If the app cannot obtain the Windows application data location, including during unpackaged runs, it can use a separate local settings folder that may not be removed with the packaged app. Local diagnostic output, if captured by another tool, follows that tool's retention settings. [Windows package data and uninstall](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/msix-windows10-windows11#msix-persistent-identity), [settings storage](src/WindowPalette.Windows/SettingsStore.cs)

## Questions and changes

For privacy questions or requests, use the maintainer's support contact published with the Microsoft Store listing when available. Do not include sensitive information in a public issue. We will update this notice if the application's practices change.
