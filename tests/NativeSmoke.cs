using System.Runtime.InteropServices;
using GTSWindowMod.Core;
using GTSWindowMod.Native;

static class NativeSmoke
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint ex, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr parent, int id);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Win32.EnumWindowsProc callback, IntPtr data);

    private static int CountChildren(IntPtr parent)
    {
        int count = 0;
        EnumChildWindows(parent, (child, data) => { count++; return true; }, IntPtr.Zero);
        return count;
    }

    internal static void Run(Action<bool, string> check)
    {
        // Owned hidden STATIC-class fixture. No ShowWindow, no game-window discovery,
        // no foreground activation and no persistent configuration is performed.
        IntPtr hwnd = CreateWindowEx(0, "STATIC", "GTSWindowMod hidden test fixture",
            unchecked((uint)Win32.NormalFrame), 100, 100, 800, 600,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Win32.Check(hwnd != IntPtr.Zero, "Create hidden fixture");
        IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Win32.MinMaxInfo)));
        try
        {
            var warnings = new List<string>();
            using (var native = new UnityWindowPlatform(hwnd, 640, 360, warnings.Add))
            {
                check(native.IsValid && !Win32.IsWindowVisible(hwnd), "native fixture belongs to this process and remains hidden");
                var extremeWork = native.WorkAreaFor(new WindowRect(int.MaxValue, int.MaxValue, 1280, 720));
                check(extremeWork.Width > 0 && extremeWork.Height > 0, "native monitor lookup tolerates extreme saved coordinates without overflow");
                check(warnings.Count == 0, "real same-thread SetWindowSubclass installs successfully");
                native.SetBorderless(false);
                check(native.HasExpectedStyle(false), "real Win32 resize/maximize/caption style bits applied");
                IntPtr menu = Win32.GetSystemMenu(hwnd, false);
                check(Win32.GetMenuState(menu, Win32.BorderlessMenuId, 0) != uint.MaxValue &&
                    Win32.GetMenuState(menu, Win32.ResetMenuId, 0) != uint.MaxValue, "native title-bar menu exposes fullscreen and reset mouse actions");
                Win32.AppendMenu(menu, 0, new UIntPtr(0xA700), "Foreign fixture entry");
                SendMessage(hwnd, Win32.SysCommand, new IntPtr(Win32.BorderlessMenuId | 3), IntPtr.Zero);
                check(native.TakeUiAction() == WindowUiAction.Borderless && native.TakeUiAction() == WindowUiAction.None,
                    "menu click queues exactly one fullscreen action and masks Windows command flag bits");
                SendMessage(hwnd, Win32.SysCommand, new IntPtr(Win32.ResetMenuId), IntPtr.Zero);
                check(native.TakeUiAction() == WindowUiAction.Reset, "native reset menu command is delivered to main-thread queue");
                var target = WindowRect.Fit(new WindowRect(140, 160, 900, 650), native.WorkAreaFor(native.Bounds));
                native.Move(target);
                check(native.Bounds.Equals(target), "real SetWindowPos/GetWindowRect roundtrip");
                var empty = new Win32.MinMaxInfo();
                Marshal.StructureToPtr(empty, block, false);
                SendMessage(hwnd, Win32.GetMinMaxInfo, IntPtr.Zero, block);
                var minmax = Marshal.PtrToStructure<Win32.MinMaxInfo>(block);
                var work = native.WorkAreaFor(native.Bounds);
                check(minmax.MinTrackSize.X >= Math.Min(640, work.Width) && minmax.MinTrackSize.Y >= Math.Min(360, work.Height), "real subclass message enforces minimum client size with frame padding");
                check(minmax.MaxSize.X == work.Width && minmax.MaxSize.Y == work.Height, "real subclass maximum bounds match the monitor work area");
                native.SetBorderless(true);
                check(native.HasExpectedStyle(true), "real borderless style removes caption and sizing frame");
                check(GetDlgItem(hwnd, 0xA730) == IntPtr.Zero && CountChildren(hwnd) == 0, "borderless creates no return-window button or other child UI");
                SendMessage(hwnd, 0x0111, new IntPtr(0xA730), IntPtr.Zero); // Former button command is inert.
                check(native.TakeUiAction() == WindowUiAction.None, "removed return-button command no longer queues a window action");
                native.Move(native.MonitorAreaFor(native.Bounds));
                check(native.Bounds.Equals(native.MonitorAreaFor(native.Bounds)), "hidden borderless fixture covers selected monitor coordinates");
                check(CountChildren(hwnd) == 0, "resizing borderless window does not recreate any child button");
                native.SetBorderless(false);
                check(CountChildren(hwnd) == 0, "normal window remains free of extra in-game UI buttons");
                native.SetGameOwned(true);
                Marshal.StructureToPtr(empty, block, false);
                SendMessage(hwnd, Win32.GetMinMaxInfo, IntPtr.Zero, block);
                var nativeOwnedInfo = Marshal.PtrToStructure<Win32.MinMaxInfo>(block);
                check(nativeOwnedInfo.MinTrackSize.X == 0 && nativeOwnedInfo.MaxSize.X == 0,
                    "native fullscreen ownership bypasses Mod minimum/maximum size constraints");
                check(Win32.GetMenuState(menu, Win32.BorderlessMenuId, 0) == uint.MaxValue,
                    "native fullscreen handoff removes Mod system-menu entries");
                native.SetGameOwned(false); native.SetBorderless(false);
                Marshal.StructureToPtr(empty, block, false);
                SendMessage(hwnd, Win32.GetMinMaxInfo, IntPtr.Zero, block);
                check(Marshal.PtrToStructure<Win32.MinMaxInfo>(block).MinTrackSize.X >= Math.Min(640, work.Width),
                    "returning to Mod ownership restores native resize constraints and UI");
                native.Move(target);
                native.Dispose();
                check(Win32.GetMenuState(menu, Win32.BorderlessMenuId, 0) == uint.MaxValue &&
                    Win32.GetMenuState(menu, Win32.ResetMenuId, 0) == uint.MaxValue, "dispose removes plugin menu entries");
                check(Win32.GetMenuState(menu, 0xA700, 0) != uint.MaxValue &&
                    Win32.GetMenuState(menu, 0xF060, 0) != uint.MaxValue, "dispose preserves other entries and native Close command");
                Marshal.StructureToPtr(empty, block, false);
                SendMessage(hwnd, Win32.GetMinMaxInfo, IntPtr.Zero, block);
                minmax = Marshal.PtrToStructure<Win32.MinMaxInfo>(block);
                check(minmax.MinTrackSize.X == 0, "subclass removal detaches plugin minimum-size handler");
                native.RestoreOriginalFrame();
                check(!Win32.IsWindowVisible(hwnd), "native style/geometry tests never display or activate the fixture");
            }
        }
        finally { Marshal.FreeHGlobal(block); DestroyWindow(hwnd); }
    }
}
