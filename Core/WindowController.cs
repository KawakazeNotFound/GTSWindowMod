using System;

namespace GTSWindowMod.Core
{
    public enum WindowMode { Windowed, Maximized, Borderless, Fullscreen }
    public enum WindowUiAction { None, Borderless, Reset }

    // All rectangles use Win32 screen coordinates, not Unity mouse coordinates.
    public struct WindowRect : IEquatable<WindowRect>
    {
        public int X, Y, Width, Height;
        public WindowRect(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Equals(WindowRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is WindowRect && Equals((WindowRect)obj);
        public override int GetHashCode() => X ^ Y ^ Width ^ Height;
        public override string ToString() => $"{Width}x{Height} at ({X},{Y})";

        public static WindowRect Fit(WindowRect value, WindowRect work)
        {
            int w = Math.Max(1, Math.Min(value.Width, work.Width));
            int h = Math.Max(1, Math.Min(value.Height, work.Height));
            // Clamp BEFORE adding so even hand-edited int.MaxValue coordinates are safe.
            int x = Math.Max(work.X, Math.Min(value.X, work.X + work.Width - w));
            int y = Math.Max(work.Y, Math.Min(value.Y, work.Y + work.Height - h));
            return new WindowRect(x, y, w, h);
        }
    }

    public interface IWindowPlatform
    {
        bool IsValid { get; }
        bool IsMinimized { get; }
        bool IsMaximized { get; }
        bool IsSizing { get; }
        bool EngineIsWindowed { get; }
        WindowRect Bounds { get; }
        WindowRect WorkAreaFor(WindowRect anchor);
        WindowRect MonitorAreaFor(WindowRect anchor);
        WindowRect DefaultWindow(WindowRect anchor);
        void PrepareEngine(WindowRect desiredOuter, bool borderless);
        void SetBorderless(bool value);
        bool HasExpectedStyle(bool borderless);
        void Restore();
        void Move(WindowRect bounds);
        void Maximize();
        void SetGameOwned(bool value);
    }

    // Pure state machine: no Unity/Win32 calls here, exercised by fake-platform tests.
    public sealed class WindowController
    {
        private readonly IWindowPlatform _platform;
        private readonly Action<WindowMode, WindowRect> _save;
        private readonly Action<string> _log;
        private WindowRect _normal, _anchor, _observed;
        private WindowMode _mode;
        private bool _pending, _hasObservation, _firstRequest = true, _sawGameFullscreen;
        private double _applyAt, _deadline, _stableSince, _nextSave;
        private WindowMode _savedMode;
        private WindowRect _savedRect;
        public bool EnforceMode { get; set; } = true;
        public bool HasTimedOut { get; private set; }
        public WindowMode Mode => _mode;
        public bool IsTransitioning => _pending;
        public bool GameOwnsDisplay => _mode == WindowMode.Fullscreen;

        public WindowController(IWindowPlatform platform, WindowRect savedNormal,
            Action<WindowMode, WindowRect> save, Action<string> log)
        {
            _platform = platform; _save = save; _log = log;
            _normal = WindowRect.Fit(savedNormal, platform.WorkAreaFor(savedNormal));
            _savedRect = _normal;
        }

        public void Request(WindowMode mode, double now)
        {
            if (!_platform.IsValid) return;
            if (mode == WindowMode.Fullscreen) { ReleaseToGame(); return; }
            if (!_pending && _hasObservation) CaptureNormal();
            _platform.SetGameOwned(false);
            _sawGameFullscreen = false;
            _mode = mode;
            // On startup, restore the saved monitor even if Unity initially opened on
            // the primary display. Later UI requests follow the live window.
            _anchor = _firstRequest ? _normal : _platform.Bounds;
            _firstRequest = false;
            WindowRect target = mode == WindowMode.Borderless ? _platform.MonitorAreaFor(_anchor) : _normal;
            _platform.PrepareEngine(target, mode == WindowMode.Borderless);
            // Unity SetResolution completes after the current frame. Do not race it.
            _applyAt = now + 0.35;
            _deadline = now + 3;
            _pending = true;
            _hasObservation = false;
        }

        // This is an ownership handoff, NOT an implementation of exclusive fullscreen.
        // The game's original ApplyGraphicsSettings/BorderlessHelper performs that switch.
        public void ReleaseToGame()
        {
            if (GameOwnsDisplay) return;
            if (!_pending && _hasObservation) CaptureNormal();
            _pending = false;
            _hasObservation = false;
            _firstRequest = false;
            _sawGameFullscreen = !_platform.EngineIsWindowed;
            _mode = WindowMode.Fullscreen;
            _platform.SetGameOwned(true);
            _log("Display ownership -> game; native fullscreen pipeline remains active.");
            Save();
        }

        public void Reset(double now)
        {
            _normal = _platform.DefaultWindow(_platform.Bounds);
            _hasObservation = false; // Do not recapture the window being reset.
            Request(WindowMode.Windowed, now);
        }

        public void Tick(double now)
        {
            if (!_platform.IsValid || _platform.IsMinimized) return;
            if (GameOwnsDisplay)
            {
                if (!_platform.EngineIsWindowed) _sawGameFullscreen = true;
                // Do not reclaim during the game's delayed fullscreen entry. Only an
                // observed return from fullscreen (or an explicit menu request) reclaims.
                else if (_sawGameFullscreen) Request(WindowMode.Windowed, now);
                return;
            }
            if (_pending)
            {
                if (now < _applyAt) return;
                if (!_platform.EngineIsWindowed)
                {
                    if (now < _deadline) return;
                    _pending = false;
                    // An engine refusing the transition must not cause a retry every frame.
                    EnforceMode = false;
                    HasTimedOut = true;
                    _log("Windowed transition timed out; automatic enforcement disabled for this session.");
                    return;
                }
                if (_platform.IsMaximized) _platform.Restore();
                _platform.SetBorderless(_mode == WindowMode.Borderless);
                if (_mode == WindowMode.Borderless)
                    _platform.Move(_platform.MonitorAreaFor(_anchor));
                else
                {
                    _normal = WindowRect.Fit(_normal, _platform.WorkAreaFor(_normal));
                    _platform.Move(_normal);
                    if (_mode == WindowMode.Maximized) _platform.Maximize();
                }
                _pending = false;
                _hasObservation = false;
                _log("Applied " + _mode + "; outer window " + _platform.Bounds);
                return;
            }

            if (!_platform.EngineIsWindowed)
            { ReleaseToGame(); return; }
            if (_platform.IsSizing) { _hasObservation = false; return; }
            if (EnforceMode && !_platform.HasExpectedStyle(_mode == WindowMode.Borderless))
                _platform.SetBorderless(_mode == WindowMode.Borderless);

            if (_mode == WindowMode.Borderless)
            {
                // Also responds to monitor/resolution changes, without disk writes per frame.
                if (EnforceMode)
                {
                    WindowRect monitor = _platform.MonitorAreaFor(_platform.Bounds);
                    if (!_platform.Bounds.Equals(monitor)) _platform.Move(monitor);
                }
            }
            else
                _mode = _platform.IsMaximized ? WindowMode.Maximized : WindowMode.Windowed;

            WindowRect current = _platform.Bounds;
            if (!_hasObservation || !_observed.Equals(current))
            { _observed = current; _stableSince = now; _hasObservation = true; return; }
            if (now - _stableSince < 0.75) return;
            CaptureNormal();
            if (now >= _nextSave && (!_normal.Equals(_savedRect) || _mode != _savedMode))
            {
                Save();
                _nextSave = now + 2;
            }
        }

        private void CaptureNormal()
        {
            if (_platform.IsValid && _platform.EngineIsWindowed && !GameOwnsDisplay && _mode != WindowMode.Borderless &&
                !_platform.IsMaximized && !_platform.IsMinimized && !_platform.IsSizing)
                _normal = _platform.Bounds;
        }

        public void Save()
        {
            // Never sample an in-flight Unity resize, minimized rect or fullscreen rect.
            if (!_pending && _hasObservation) CaptureNormal();
            _save(_mode, _normal);
            _savedMode = _mode; _savedRect = _normal;
        }
    }
}
