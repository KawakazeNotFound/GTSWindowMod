using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GTSWindowMod.Core;

namespace GTSWindowMod.Native
{
    internal static class Win32
    {
        internal const int GwlStyle = -16, GwlExStyle = -20;
        internal const long Popup = 0x80000000L, Caption = 0x00C00000L, ThickFrame = 0x00040000L;
        internal const long SysMenu = 0x00080000L, MinimizeBox = 0x00020000L, MaximizeBox = 0x00010000L;
        internal const long NormalFrame = Caption | ThickFrame | SysMenu | MinimizeBox | MaximizeBox;
        internal const long EdgeStyles = 0x00000100L | 0x00000200L | 0x00000001L | 0x00020000L;
        internal const uint NoZOrder = 0x0004, NoActivate = 0x0010, FrameChanged = 0x0020, NoMove = 0x0002, NoSize = 0x0001;
        internal const uint GetMinMaxInfo = 0x0024, EnterSizeMove = 0x0231, ExitSizeMove = 0x0232;
        internal const uint SysCommand = 0x0112;
        // User system-menu identifiers must be below 0xF000 and aligned to 16.
        internal const uint BorderlessMenuId = 0xA710, ResetMenuId = 0xA720;
        internal static readonly UIntPtr SubclassId = new UIntPtr(0x47574D01u);

        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public WindowRect ToWindowRect() => new WindowRect(Left, Top, Right - Left, Bottom - Top);
            public static Rect From(WindowRect r) => new Rect
            {
                Left = r.X, Top = r.Y,
                Right = (int)Math.Min(int.MaxValue, (long)r.X + Math.Max(1, r.Width)),
                Bottom = (int)Math.Min(int.MaxValue, (long)r.Y + Math.Max(1, r.Height))
            };
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo
        { public int Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo
        { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] internal static extern bool RemoveMenu(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetMenuState(IntPtr menu, uint id, uint flags);
        [DllImport("user32.dll")] internal static extern bool DrawMenuBar(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool AdjustWindowRectEx(ref Rect rect, uint style, bool menu, uint exStyle);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetLong32(IntPtr hwnd, int index, int value);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("comctl32.dll", SetLastError = true)] internal static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")] internal static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        internal static long GetStyle(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetLong64(hwnd, index).ToInt64() & 0xffffffffL : (uint)GetLong32(hwnd, index);
        internal static void SetStyle(IntPtr hwnd, int index, long value)
        {
            SetLastError(0);
            IntPtr old = IntPtr.Size == 8 ? SetLong64(hwnd, index, new IntPtr(value)) : new IntPtr(SetLong32(hwnd, index, unchecked((int)value)));
            int error = Marshal.GetLastWin32Error();
            if (old == IntPtr.Zero && error != 0) throw new Win32Exception(error, "SetWindowLongPtr");
        }
        internal static void Check(bool ok, string operation)
        { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), operation); }

        internal static IntPtr FindGameWindow()
        {
            IntPtr found = IntPtr.Zero;
            uint ownProcess = GetCurrentProcessId();
            EnumWindows((hwnd, data) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != ownProcess || !IsWindowVisible(hwnd)) return true;
                var name = new StringBuilder(256);
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = hwnd; return false;
            }, IntPtr.Zero);
            return found;
        }
        internal static MonitorInfo MonitorFor(WindowRect anchor)
        {
            Rect rect = Rect.From(anchor);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            Check(GetMonitorInfo(MonitorFromRect(ref rect, 2), ref info), "GetMonitorInfo");
            return info;
        }
    }
}
