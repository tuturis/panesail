using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WindowPalette.Windows.Native;

/// <summary>A disposable, content-free desktop window for real title-bar drag acceptance checks.</summary>
internal static class DesktopFixture
{
    private const string ClassName = "WindowPalette.TestFixture";
    private const uint RefreshMessage = 0x8001;
    private static nint _window, _label;
    private static readonly NativeMethods.WindowProc Procedure = WindowProcedure;

    internal static int Run()
    {
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(-4);
        var module = NativeMethods.GetModuleHandleW(null);
        var windowClass = new NativeMethods.WindowClass
        {
            Procedure = Procedure,
            Instance = module,
            ClassName = ClassName,
            Background = 6, // COLOR_WINDOW + 1: a system-owned brush.
        };
        var registered = false;
        try
        {
            if (NativeMethods.RegisterClassW(ref windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The desktop test window could not be registered.");
            registered = true;
            if (!WindowSnapshot.TryMonitor(NativeMethods.MonitorFromPoint(default, 1), out var monitor))
                throw new InvalidOperationException("The primary screen could not be read.");

            var work = monitor.WorkArea;
            var width = Math.Max(320, (int)Math.Round(work.Width * 0.70));
            var height = Math.Max(220, (int)Math.Round(work.Height * 0.50));
            var left = work.Left + Math.Min(100, Math.Max(0, work.Width - width));
            var top = work.Top + Math.Min(100, Math.Max(0, work.Height - height));
            _window = NativeMethods.CreateWindowExW(0, ClassName, "Window Palette test window", 0x10CF0000,
                left, top, width, height, 0, 0, module, 0); // Visible, normal resizable overlapped window.
            if (_window == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The desktop test window could not be created.");

            _label = NativeMethods.CreateWindowExW(0, "STATIC", "", 0x50000000,
                20, 20, Math.Max(200, width - 60), Math.Max(150, height - 70), _window, 0, module, 0);
            if (_label == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The desktop test instructions could not be created.");
            SendMessageW(_label, 0x0030, (nuint)GetStockObject(17), 1); // WM_SETFONT; DEFAULT_GUI_FONT.
            // A hidden console launcher can pass SW_HIDE in STARTUPINFO. The first call consumes
            // that startup hint; the explicit second call makes the disposable fixture visible.
            ShowWindow(_window, 1);
            ShowWindow(_window, 1); // SW_SHOWNORMAL
            NativeMethods.PostMessageW(_window, RefreshMessage, 0, 0);
            using var expiry = new Timer(_ => NativeMethods.PostMessageW(_window, NativeMethods.WmClose, 0, 0),
                null, TimeSpan.FromMinutes(20), Timeout.InfiniteTimeSpan);
            Console.WriteLine("A content-free test window is open. It closes automatically after twenty minutes.");
            while (true)
            {
                var result = NativeMethods.GetMessageW(out var message, 0, 0, 0);
                if (result == 0) return unchecked((int)message.WParam);
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessageW(in message);
            }
        }
        finally
        {
            if (_window != 0 && NativeMethods.IsWindow(_window)) NativeMethods.DestroyWindow(_window);
            _window = _label = 0;
            if (registered) NativeMethods.UnregisterClassW(ClassName, module);
            if (previousDpi != 0) NativeMethods.SetThreadDpiAwarenessContext(previousDpi);
            GC.KeepAlive(Procedure);
        }
    }

    internal static string ReadDiagnostics()
    {
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(-4);
        try
        {
            var text = new StringBuilder();
            var window = FindWindowW(ClassName, null);
            if (window != 0 && WindowSnapshot.TryVisibleBounds(window, out var bounds))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"Test window visible bounds: ({bounds.Left}, {bounds.Top}), {bounds.Width} × {bounds.Height} pixels.");
                text.AppendLine(CultureInfo.InvariantCulture, $"Test window DPI: {NativeMethods.GetDpiForWindow(window)}.");
                var identified = WindowSnapshot.TryIdentity(window, out var identity);
                var captionPoint = new WindowPalette.Core.PixelPoint(bounds.Left + bounds.Width / 2, bounds.Top + 12);
                text.AppendLine(CultureInfo.InvariantCulture, $"Visible: {NativeMethods.IsWindowVisible(window)}; identity readable: {identified}; normal resizable: {identified && WindowSnapshot.IsNormalResizable(identity)}; caption recognized: {WindowSnapshot.IsCaption(window, captionPoint)}.");
            }
            else
            {
                text.AppendLine("No test window is open.");
            }

            var screens = new List<string>();
            NativeMethods.MonitorEnumProc callback = (nint monitorHandle, nint hdc, ref NativeMethods.Rect rect, nint data) =>
            {
                if (!WindowSnapshot.TryMonitor(monitorHandle, out var monitor)) return true;
                var dpiResult = NativeMethods.GetDpiForMonitor(monitorHandle, 0, out var dpiX, out var dpiY);
                var screen = monitor.Bounds;
                var work = monitor.WorkArea;
                var dpi = dpiResult == 0 ? $"{dpiX} × {dpiY}" : "unavailable";
                screens.Add($"Screen at ({screen.Left}, {screen.Top}): {screen.Width} × {screen.Height}; usable ({work.Left}, {work.Top}), {work.Width} × {work.Height}; DPI {dpi}.");
                return true;
            };
            NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
            foreach (var screen in screens) text.AppendLine(screen);
            return text.ToString();
        }
        finally
        {
            if (previousDpi != 0) NativeMethods.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    internal static bool Close()
    {
        var window = FindWindowW(ClassName, null);
        return window != 0 && NativeMethods.PostMessageW(window, NativeMethods.WmClose, 0, 0);
    }

    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case NativeMethods.WmClose:
                    NativeMethods.DestroyWindow(window);
                    return 0;
                case 0x0002: // WM_DESTROY
                    NativeMethods.PostQuitMessage(0);
                    return 0;
                case 0x02E0: // WM_DPICHANGED: behave like an ordinary per-monitor-aware application.
                    var suggested = Marshal.PtrToStructure<NativeMethods.Rect>(lParam);
                    NativeMethods.SetWindowPos(window, 0, suggested.Left, suggested.Top,
                        suggested.Right - suggested.Left, suggested.Bottom - suggested.Top, 0x0004 | 0x0010);
                    NativeMethods.PostMessageW(window, RefreshMessage, 0, 0);
                    return 0;
                case 0x0024: // WM_GETMINMAXINFO: exercise realistic application minimum dimensions.
                    var sizes = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    var scale = Math.Max(96u, NativeMethods.GetDpiForWindow(window)) / 96.0;
                    sizes.Minimum.X = (int)Math.Ceiling(320 * scale);
                    sizes.Minimum.Y = (int)Math.Ceiling(220 * scale);
                    Marshal.StructureToPtr(sizes, lParam, false);
                    return 0;
                case 0x0047: // WM_WINDOWPOSCHANGED
                    var result = NativeMethods.DefWindowProcW(window, message, wParam, lParam);
                    NativeMethods.PostMessageW(window, RefreshMessage, 0, 0);
                    return result;
                case RefreshMessage:
                    Refresh(window);
                    return 0;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Desktop fixture: " + exception.Message);
            NativeMethods.PostQuitMessage(1);
            return 0;
        }

        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private static void Refresh(nint window)
    {
        if (_label == 0 || !WindowSnapshot.TryVisibleBounds(window, out var bounds)) return;
        var details = "Drag this window by its title bar to another screen, then release it.\r\n\r\n"
            + "Use Window Palette to choose Match screen proportions or Keep window shape.\r\n"
            + "Shift-drag remains available for FancyZones. Escape cancels a drag.\r\n\r\n"
            + $"Visible size: {bounds.Width} × {bounds.Height} pixels\r\n"
            + $"Visible position: {bounds.Left}, {bounds.Top}\r\n"
            + $"Window DPI: {NativeMethods.GetDpiForWindow(window)}\r\n";
        if (WindowSnapshot.TryMonitor(NativeMethods.MonitorFromWindow(window, 0), out var monitor))
        {
            details += $"Usable screen: {monitor.WorkArea.Width} × {monitor.WorkArea.Height} pixels\r\n"
                + $"Screen coverage: {100.0 * bounds.Width / monitor.WorkArea.Width:F1}% wide, {100.0 * bounds.Height / monitor.WorkArea.Height:F1}% tall\r\n";
        }

        details += "\r\nThis window contains no private content. It closes automatically after twenty minutes.";
        SetWindowTextW(window, $"Window Palette test window — {bounds.Width} × {bounds.Height}");
        SetWindowTextW(_label, details);
        if (GetClientRect(window, out var client))
            NativeMethods.SetWindowPos(_label, 0, 20, 20, Math.Max(1, client.Right - 40), Math.Max(1, client.Bottom - 40), 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        internal NativeMethods.Point Reserved, MaximumSize, MaximumPosition, Minimum, Maximum;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string? title);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowTextW(nint window, string text);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out NativeMethods.Rect bounds);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
}
