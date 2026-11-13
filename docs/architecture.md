# Architecture

PaneSail has three responsibilities: Command Palette controls, window-management decisions, and Windows integration. The feature runs in a separate background process after the user enables it; that process can continue when Command Palette closes. [Controls](../src/WindowPalette.CmdPal/Pages/DemoPage.cs), [background component](../src/WindowPalette.Windows/BackgroundProcess.cs)

The following diagram is an **explanatory model**, not a recorded session:

```text
Command Palette → controls and settings → background component
                                         ↓
                                drag and display observations
                                         ↓
                                eligibility and size decision
                                         ↓
                                at most one window adjustment
```

The geometry library determines whether the released window already fits and calculates the desired result for oversized windows. The Windows integration observes input and movement events, checks gesture eligibility, obtains monitor and window bounds, and applies a request. The Command Palette host presents settings and status. [Geometry](../src/WindowPalette.Core/Geometry.cs), [gesture policy](../src/WindowPalette.Core/GesturePolicy.cs), [Windows monitor](../src/WindowPalette.Windows/WindowEventMonitor.cs)

The initial experience handles eligible title-bar mouse drags between monitors. It skips ambiguous or unsupported gestures and leaves fitting windows unchanged; exact placement remains subject to operating-system and application constraints. Shift-drag and observed snap gestures are bypassed conservatively, without a guarantee of compatibility with every window manager. [Fit decision](../src/WindowPalette.Windows/WindowResizeOperation.cs), [window eligibility](../src/WindowPalette.Windows/Native/WindowSnapshot.cs)

PaneSail adapts the Microsoft PowerToys Command Palette extension template and is an independent project. See the [third-party notices](../THIRD-PARTY-NOTICES.md) for attribution and license terms.
