using System.Runtime.InteropServices;

namespace WindowPalette.Windows.Native;

internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13, WhMouseLl = 14;
    internal const uint MoveStart = 0x000A, MoveEnd = 0x000B, ArrangementPreview = 0x8016;
    internal const uint WmClose = 0x0010, WmQuit = 0x0012, WmDisplayChange = 0x007E, WmSettingChange = 0x001A;
    internal const uint WmLeftDown = 0x0201, WmLeftUp = 0x0202, WmRightDown = 0x0204, WmRightUp = 0x0205;
    internal const uint WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
    internal const int VkShift = 0x10, VkEscape = 0x1B, VkLeftShift = 0xA0, VkRightShift = 0xA1;

    internal delegate nint HookProc(int code, nuint message, nint data);
    internal delegate void WinEventProc(nint hook, uint eventId, nint hwnd, int objectId, int childId, uint thread, uint time);
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect bounds, nint data);

    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { internal Point Point; internal uint MouseDataValue, Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VirtualKey, ScanCode, Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { internal nint Hwnd; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Point; internal uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal uint Size; internal Rect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo { internal uint Size, Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
    {
        internal uint Style;
        internal WindowProc Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? MenuName;
        internal string ClassName;
    }

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookExW(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterClassW(string className, nint instance);
    [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern int GetMessageW(out Message message, nint hwnd, uint minimum, uint maximum);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(in Message message);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll")] internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SendMessageTimeoutW(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] internal static extern int DwmGetWindowIntAttribute(nint hwnd, uint attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetTokenInformation(nint token, int informationClass, out int value, uint length, out uint returned);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(nint handle);
}
