using System.Globalization;
using System.Runtime.InteropServices;
using WindowPalette.Core;

namespace WindowPalette.Windows.Native;

internal readonly record struct WindowIdentity(nint Handle, uint Process, uint Thread)
{
    internal bool StillExists => NativeMethods.IsWindow(Handle)
        && NativeMethods.GetWindowThreadProcessId(Handle, out var process) == Thread && process == Process;
}

internal readonly record struct MonitorSnapshot(nint Handle, PixelRect Bounds, PixelRect WorkArea);

internal static class WindowSnapshot
{
    internal static PixelRect ToPixels(NativeMethods.Rect rectangle) => new(
        rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    internal static NativeMethods.Point ToNative(PixelPoint point) => new() { X = point.X, Y = point.Y };

    internal static bool TryVisibleBounds(nint window, out PixelRect bounds)
    {
        bounds = default;
        if (NativeMethods.DwmGetWindowAttribute(window, 9, out var rect, Marshal.SizeOf<NativeMethods.Rect>()) != 0)
        {
            return false;
        }

        bounds = ToPixels(rect);
        return bounds.IsValid;
    }

    internal static bool TryMonitor(nint handle, out MonitorSnapshot snapshot)
    {
        snapshot = default;
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (handle == 0 || !NativeMethods.GetMonitorInfoW(handle, ref info))
        {
            return false;
        }

        snapshot = new(handle, ToPixels(info.Monitor), ToPixels(info.Work));
        return snapshot.Bounds.IsValid && snapshot.WorkArea.IsValid;
    }

    internal static bool TryIdentity(nint window, out WindowIdentity identity)
    {
        var thread = NativeMethods.GetWindowThreadProcessId(window, out var process);
        identity = new(window, process, thread);
        return window != 0 && thread != 0 && process != 0 && process != (uint)Environment.ProcessId;
    }

    internal static bool IsNormalResizable(WindowIdentity identity)
    {
        var window = identity.Handle;
        if (!identity.StillExists || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)
            || NativeMethods.IsZoomed(window) || NativeMethods.GetAncestor(window, 2) != window
            || NativeMethods.GetWindow(window, 4) != 0)
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtrW(window, -16).ToInt64();
        var extendedStyle = NativeMethods.GetWindowLongPtrW(window, -20).ToInt64();
        if ((style & 0x00040000L) == 0 || (style & 0x40000000L) != 0 || (extendedStyle & 0x80L) != 0)
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowIntAttribute(window, 14, out var cloaked, sizeof(int)) != 0 || cloaked != 0)
        {
            return false;
        }

        return IsUnelevated(identity.Process);
    }

    private static bool IsUnelevated(uint processId)
    {
        var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process == 0)
        {
            return false;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, 0x0008, out var token))
            {
                return false;
            }

            try
            {
                return NativeMethods.GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated == 0;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    internal static bool IsCaption(nint window, PixelPoint point)
    {
        // WM_NCHITTEST takes signed 16-bit screen coordinates in LPARAM.
        // Coordinates outside that API's range cannot be positively classified.
        if (point.X is < short.MinValue or > short.MaxValue || point.Y is < short.MinValue or > short.MaxValue)
        {
            return false;
        }

        var packed = unchecked((nint)(((uint)(ushort)point.Y << 16) | (ushort)point.X));
        return NativeMethods.SendMessageTimeoutW(window, 0x0084, 0, packed, 0x0002 | 0x0001, 60, out var hit) != 0
            && unchecked((int)hit) == 2;
    }

    internal static bool IsFullscreen(PixelRect visible, MonitorSnapshot monitor) =>
        visible.Left <= monitor.Bounds.Left + 2 && visible.Top <= monitor.Bounds.Top + 2
        && visible.Right >= monitor.Bounds.Right - 2 && visible.Bottom >= monitor.Bounds.Bottom - 2;

    internal static bool TryTopology(out string topology)
    {
        topology = string.Empty;
        var displays = new List<string>();
        NativeMethods.MonitorEnumProc callback = (nint handle, nint hdc, ref NativeMethods.Rect rectangle, nint data) =>
        {
            if (!TryMonitor(handle, out var monitor))
            {
                return false;
            }

            if (NativeMethods.GetDpiForMonitor(handle, 0, out var dpiX, out var dpiY) != 0)
            {
                return false;
            }
            displays.Add(string.Create(CultureInfo.InvariantCulture,
                $"{handle}:{monitor.Bounds.Left},{monitor.Bounds.Top},{monitor.Bounds.Width},{monitor.Bounds.Height}:{monitor.WorkArea.Left},{monitor.WorkArea.Top},{monitor.WorkArea.Width},{monitor.WorkArea.Height}:{dpiX},{dpiY}"));
            return true;
        };
        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0) || displays.Count == 0)
        {
            return false;
        }

        displays.Sort(StringComparer.Ordinal);
        topology = string.Join(";", displays);
        return true;
    }

    internal static bool SafeDrop(PixelPoint point, MonitorSnapshot monitor, nint window)
    {
        var dpi = NativeMethods.GetDpiForWindow(window);
        var edge = (int)Math.Ceiling(24 * Math.Max(96u, dpi) / 96.0);
        var work = monitor.WorkArea;
        // Only the final release is tested. Crossing a seam during the drag is normal.
        return point.X >= work.Left + edge && point.X < work.Right - edge
            && point.Y >= work.Top + edge && point.Y < work.Bottom - edge;
    }

    internal static bool TryApplyVisibleBounds(nint window, PixelRect desired)
    {
        if (!TryVisibleBounds(window, out var visible) || !NativeMethods.GetWindowRect(window, out var outerNative))
        {
            return false;
        }

        var outer = ToPixels(outerNative);
        var leftMargin = visible.Left - outer.Left;
        var topMargin = visible.Top - outer.Top;
        var rightMargin = outer.Right - visible.Right;
        var bottomMargin = outer.Bottom - visible.Bottom;
        return NativeMethods.SetWindowPos(window, 0,
            desired.Left - leftMargin, desired.Top - topMargin,
            desired.Width + leftMargin + rightMargin, desired.Height + topMargin + bottomMargin,
            0x0004 | 0x0010 | 0x0200 | 0x4000); // NOZORDER | NOACTIVATE | NOOWNERZORDER | ASYNCWINDOWPOS
    }
}
