using System;
using System.Runtime.InteropServices;
using System.Threading;
using GTSWindowMod.Core;
using UnityEngine;

namespace GTSWindowMod.Native
{
    internal sealed class UnityWindowPlatform : IWindowPlatform, IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly long _originalStyle, _originalExStyle;
        private readonly Win32.SubclassProc _callback; // Rooted for the entire subclass lifetime.
        private readonly Action<string> _log;
        private readonly int _minWidth, _minHeight;
        private bool _subclassed;
        private IntPtr _systemMenu;
        private bool _borderlessMenuAdded, _resetMenuAdded;
        private int _pendingUiAction;
        private volatile bool _borderless, _sizing, _gameOwned;
        public IntPtr Handle => _hwnd;
        public bool IsValid
        {
            get
            {
                if (!Win32.IsWindow(_hwnd)) return false;
                Win32.GetWindowThreadProcessId(_hwnd, out uint owner);
                return owner == Win32.GetCurrentProcessId();
            }
        }
        public bool IsMinimized => Win32.IsIconic(_hwnd);
        public bool IsMaximized => Win32.IsZoomed(_hwnd);
        public bool IsSizing => _sizing;
        public bool EngineIsWindowed => Screen.fullScreenMode == FullScreenMode.Windowed;
        public WindowRect Bounds
        {
            get { Win32.Check(Win32.GetWindowRect(_hwnd, out var rect), "GetWindowRect"); return rect.ToWindowRect(); }
        }

        internal UnityWindowPlatform(IntPtr hwnd, int minWidth, int minHeight, Action<string> log)
        {
            _hwnd = hwnd; _log = log; _minWidth = minWidth; _minHeight = minHeight;
            _originalStyle = Win32.GetStyle(hwnd, Win32.GwlStyle);
            _originalExStyle = Win32.GetStyle(hwnd, Win32.GwlExStyle);
            _callback = WindowMessage;
            uint windowThread = Win32.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != Win32.GetCurrentProcessId()) throw new ArgumentException("Window must belong to the current process.", nameof(hwnd));
            // SetWindowSubclass is documented as same-thread only.
            if (windowThread == Win32.GetCurrentThreadId())
                _subclassed = Win32.SetWindowSubclass(hwnd, _callback, Win32.SubclassId, UIntPtr.Zero);
            if (!_subclassed) log("Window subclass unavailable; native resize/maximize remain enabled, custom menu and size-constraint handling inactive.");
        }

        public WindowUiAction TakeUiAction() => (WindowUiAction)Interlocked.Exchange(ref _pendingUiAction, 0);

        public void SetGameOwned(bool value)
        {
            if (_gameOwned == value) return;
            // Normalize our popup frame once, before the game enters fullscreen; never
            // change frame/position when it has ALREADY entered native fullscreen.
            if (value && EngineIsWindowed && _borderless) SetBorderless(false);
            _gameOwned = value;
            if (value)
            {
                _borderless = false;
                RemoveOwnMenu();
                Interlocked.Exchange(ref _pendingUiAction, 0);
            }
        }

        public WindowRect WorkAreaFor(WindowRect anchor) => Win32.MonitorFor(anchor).Work.ToWindowRect();
        public WindowRect MonitorAreaFor(WindowRect anchor) => Win32.MonitorFor(anchor).Monitor.ToWindowRect();
        public WindowRect DefaultWindow(WindowRect anchor)
        {
            var work = WorkAreaFor(anchor);
            var frame = FramePadding(false);
            int w = Math.Min(work.Width, 1280 + frame.Width);
            int h = Math.Min(work.Height, 720 + frame.Height);
            return new WindowRect(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
        }

        private long DesiredStyle(bool borderless)
        {
            long style = Win32.GetStyle(_hwnd, Win32.GwlStyle);
            return borderless ? (style & ~Win32.NormalFrame) | Win32.Popup
                : (style & ~Win32.Popup) | Win32.NormalFrame;
        }
        private long DesiredExStyle(bool borderless)
        {
            long style = Win32.GetStyle(_hwnd, Win32.GwlExStyle);
            // Restore only frame-edge bits. Leave unrelated extended styles untouched.
            return (style & ~Win32.EdgeStyles) | (borderless ? 0 : _originalExStyle & Win32.EdgeStyles);
        }
        private WindowRect FramePadding(bool borderless)
        {
            var rect = new Win32.Rect();
            Win32.Check(Win32.AdjustWindowRectEx(ref rect, unchecked((uint)DesiredStyle(borderless)), false,
                unchecked((uint)DesiredExStyle(borderless))), "AdjustWindowRectEx");
            return rect.ToWindowRect();
        }

        public void PrepareEngine(WindowRect desiredOuter, bool borderless)
        {
            // Keep the engine in real Windowed mode in the three Mod-owned modes. Native borderless
            // avoids Unity 2019 fullscreen monitor selection / exclusive-fullscreen paths.
            // When already windowed, WM_SIZE drives the render size; no redundant reset.
            if (EngineIsWindowed) return;
            var padding = FramePadding(borderless);
            Screen.SetResolution(Math.Max(1, desiredOuter.Width - padding.Width),
                Math.Max(1, desiredOuter.Height - padding.Height), FullScreenMode.Windowed);
        }
        public bool HasExpectedStyle(bool borderless) =>
            Win32.GetStyle(_hwnd, Win32.GwlStyle) == DesiredStyle(borderless) &&
            Win32.GetStyle(_hwnd, Win32.GwlExStyle) == DesiredExStyle(borderless);

        public void SetBorderless(bool value)
        {
            _borderless = value;
            Win32.SetStyle(_hwnd, Win32.GwlStyle, DesiredStyle(value));
            Win32.SetStyle(_hwnd, Win32.GwlExStyle, DesiredExStyle(value));
            Win32.Check(Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.NoMove | Win32.NoSize | Win32.NoZOrder | Win32.NoActivate | Win32.FrameChanged), "Refresh window frame");
            if (!value) EnsureSystemMenu();
        }
        public void Restore() => Win32.ShowWindow(_hwnd, 9); // SW_RESTORE
        public void Maximize() => Win32.ShowWindow(_hwnd, 3); // SW_MAXIMIZE
        public void Move(WindowRect rect) => Win32.Check(Win32.SetWindowPos(_hwnd, IntPtr.Zero,
            rect.X, rect.Y, rect.Width, rect.Height, Win32.NoActivate | Win32.NoZOrder), "Move game window");

        private void EnsureSystemMenu()
        {
            if (!_subclassed) return;
            IntPtr menu = Win32.GetSystemMenu(_hwnd, false);
            if (menu == IntPtr.Zero) return;
            if (menu != _systemMenu)
            { _systemMenu = menu; _borderlessMenuAdded = false; _resetMenuAdded = false; }
            if (!_borderlessMenuAdded && Win32.GetMenuState(menu, Win32.BorderlessMenuId, 0) == uint.MaxValue)
                _borderlessMenuAdded = Win32.AppendMenu(menu, 0, new UIntPtr(Win32.BorderlessMenuId), "无边框全屏");
            if (!_resetMenuAdded && Win32.GetMenuState(menu, Win32.ResetMenuId, 0) == uint.MaxValue)
                _resetMenuAdded = Win32.AppendMenu(menu, 0, new UIntPtr(Win32.ResetMenuId), "重置窗口位置");
            Win32.DrawMenuBar(_hwnd);
        }

        private IntPtr WindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            if (msg == Win32.SysCommand)
            {
                uint command = unchecked((uint)wParam.ToInt64()) & 0xFFF0;
                if (command == Win32.BorderlessMenuId && _borderlessMenuAdded)
                {
                    Interlocked.Exchange(ref _pendingUiAction, (int)WindowUiAction.Borderless);
                    return IntPtr.Zero;
                }
                if (command == Win32.ResetMenuId && _resetMenuAdded)
                {
                    Interlocked.Exchange(ref _pendingUiAction, (int)WindowUiAction.Reset);
                    return IntPtr.Zero;
                }
            }
            if (msg == 0x0082) // WM_NCDESTROY: stop holding a callback for a dead HWND.
            {
                Win32.RemoveWindowSubclass(hwnd, _callback, Win32.SubclassId);
                _subclassed = false;
            }
            if (msg == Win32.EnterSizeMove) _sizing = true;
            if (msg == Win32.ExitSizeMove) _sizing = false;
            IntPtr result = Win32.DefSubclassProc(hwnd, msg, wParam, lParam);
            if (msg != Win32.GetMinMaxInfo || _borderless || _gameOwned || lParam == IntPtr.Zero) return result;
            // No Unity APIs or logging on the native callback path. Preserve other messages.
            try
            {
                var info = (Win32.MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(Win32.MinMaxInfo));
                var monitor = Win32.MonitorFor(Bounds);
                var work = monitor.Work.ToWindowRect();
                var padding = FramePadding(false);
                info.MinTrackSize.X = Math.Min(work.Width, _minWidth + padding.Width);
                info.MinTrackSize.Y = Math.Min(work.Height, _minHeight + padding.Height);
                info.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left;
                info.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
                info.MaxSize.X = work.Width; info.MaxSize.Y = work.Height;
                // Keep a generous drag range for monitors larger than the primary display.
                info.MaxTrackSize.X = Math.Max(info.MaxTrackSize.X, monitor.Monitor.Right - monitor.Monitor.Left);
                info.MaxTrackSize.Y = Math.Max(info.MaxTrackSize.Y, monitor.Monitor.Bottom - monitor.Monitor.Top);
                Marshal.StructureToPtr(info, lParam, false);
            }
            catch { /* Exceptions must never unwind across a native WindowProc boundary. */ }
            return result;
        }

        private void RemoveOwnMenu()
        {
            if (_systemMenu != IntPtr.Zero)
            {
                // Remove only our entries; never reset another plugin's menu changes.
                if (_borderlessMenuAdded) Win32.RemoveMenu(_systemMenu, Win32.BorderlessMenuId, 0);
                if (_resetMenuAdded) Win32.RemoveMenu(_systemMenu, Win32.ResetMenuId, 0);
                _borderlessMenuAdded = false; _resetMenuAdded = false;
                _systemMenu = IntPtr.Zero;
            }
        }
        public void Dispose()
        {
            RemoveOwnMenu();
            if (_subclassed && IsValid)
            {
                if (!Win32.RemoveWindowSubclass(_hwnd, _callback, Win32.SubclassId))
                {
                    // Retain the delegate if a third-party subclass prevents removal.
                    RetainedCallbacks.Keep(_callback);
                    _log("Window subclass removal failed; callback retained until process exit.");
                }
            }
            _subclassed = false;
        }
        internal void RestoreOriginalFrame()
        {
            if (!IsValid) return;
            // Do not overwrite live WS_VISIBLE / WS_MINIMIZE / WS_MAXIMIZE state.
            long current = Win32.GetStyle(_hwnd, Win32.GwlStyle);
            Win32.SetStyle(_hwnd, Win32.GwlStyle, (current & ~(Win32.NormalFrame | Win32.Popup)) | (_originalStyle & (Win32.NormalFrame | Win32.Popup)));
            long ex = Win32.GetStyle(_hwnd, Win32.GwlExStyle);
            Win32.SetStyle(_hwnd, Win32.GwlExStyle, (ex & ~Win32.EdgeStyles) | (_originalExStyle & Win32.EdgeStyles));
            Win32.Check(Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32.NoMove | Win32.NoSize | Win32.NoZOrder | Win32.NoActivate | Win32.FrameChanged), "Restore original frame");
        }
        private static class RetainedCallbacks
        {
            private static readonly System.Collections.Generic.List<Win32.SubclassProc> Items = new System.Collections.Generic.List<Win32.SubclassProc>();
            internal static void Keep(Win32.SubclassProc callback) => Items.Add(callback);
        }
    }
}
