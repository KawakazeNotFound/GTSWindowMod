using System;
using BepInEx;
using BepInEx.Configuration;
using GTSWindowMod.Core;
using GTSWindowMod.Native;
using UnityEngine;

namespace GTSWindowMod
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("GiantessSandbox.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.giantess.windowmod";
        public const string Name = "GTSWindowMod";
        public const string Version = "1.0.0";
        private ConfigEntry<bool> _enabled, _remember, _enforce;
        private ConfigEntry<WindowMode> _mode;
        private ConfigEntry<int> _x, _y, _width, _height, _minWidth, _minHeight;
        private UnityWindowPlatform _platform;
        private WindowController _controller;
        private GameDisplayIntegration _integration;
        private GameDisplayHooks _hooks;
        private bool _saving, _modeChanged, _failed, _quitting, _syncManagedOnReady, _requestNativeFullscreen;
        private float _nextFind = 2, _nextTick, _nextUiRefresh;

        private void Awake()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            { Logger.LogWarning("This plugin targets Windows only."); enabled = false; return; }
            Config.SaveOnConfigSet = false;
            _enabled = Config.Bind("General", "Enabled", true, "Enable window enhancement. Restart the game after changing this option.");
            _mode = Config.Bind("General", "Mode", WindowMode.Maximized, "Windowed / Maximized / Borderless: Mod-owned; Fullscreen: game-owned native fullscreen. Updated when you change mode.");
            _remember = Config.Bind("General", "RememberWindow", true, "Remember normal-window outer bounds and the most recent mode. If false, start centered using the configured Mode.");
            _enforce = Config.Bind("General", "KeepWindowMode", true, "Maintain Mod-owned frame/bounds. Native fullscreen always releases ownership to the game.");
            _minWidth = Config.Bind("Window", "MinimumClientWidth", 640,
                new ConfigDescription("Minimum client width when dragging. Restart after changing.", new AcceptableValueRange<int>(320, 7680)));
            _minHeight = Config.Bind("Window", "MinimumClientHeight", 360,
                new ConfigDescription("Minimum client height when dragging. Restart after changing.", new AcceptableValueRange<int>(200, 4320)));
            _x = Config.Bind("SavedWindow", "X", int.MinValue, "Normal-window outer X, in Win32 screen coordinates. Auto-managed. int.MinValue selects centered defaults.");
            _y = Config.Bind("SavedWindow", "Y", int.MinValue, "Normal-window outer Y, in Win32 screen coordinates. Auto-managed.");
            _width = Config.Bind("SavedWindow", "Width", 1280,
                new ConfigDescription("Normal-window OUTER width, including frame. Auto-managed.", new AcceptableValueRange<int>(320, 16384)));
            _height = Config.Bind("SavedWindow", "Height", 760,
                new ConfigDescription("Normal-window OUTER height, including frame. Auto-managed.", new AcceptableValueRange<int>(200, 16384)));
            _mode.SettingChanged += (sender, args) => { if (!_saving) _modeChanged = true; };
            Config.Save();
            if (!_enabled.Value) { enabled = false; return; }
            try
            {
                _integration = new GameDisplayIntegration(name => Type.GetType(name + ", Assembly-CSharp", false),
                    () => _controller, () => Time.realtimeSinceStartup, Logger.LogWarning);
                _hooks = new GameDisplayHooks(_integration, SuspendIntegration);
                Logger.LogInfo("Validated native graphics integration; fullscreen delegates to the game, windowed modes to the Mod.");
            }
            catch (Exception ex) { SuspendIntegration(ex); return; }
            Logger.LogInfo($"{Name} {Version} ready. Mouse-only native window controls; no hotkeys or LLM dependency.");
        }

        private void Update()
        {
            if (_failed || !_enabled.Value) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (_platform == null || !_platform.IsValid)
                {
                    if (now < _nextFind) return;
                    _nextFind = now + 1;
                    if (_platform != null) { _controller?.Save(); _platform.Dispose(); _platform = null; _controller = null; }
                    IntPtr hwnd = Win32.FindGameWindow();
                    if (hwnd == IntPtr.Zero) return;
                    _platform = new UnityWindowPlatform(hwnd, _minWidth.Value, _minHeight.Value, Logger.LogWarning);
                    var normal = _remember.Value && _x.Value != int.MinValue && _y.Value != int.MinValue
                        ? new WindowRect(_x.Value, _y.Value, _width.Value, _height.Value)
                        : _platform.DefaultWindow(_platform.Bounds);
                    _controller = new WindowController(_platform, normal, SaveWindow, Logger.LogInfo);
                    _controller.EnforceMode = _enforce.Value;
                    if (!_platform.EngineIsWindowed || _mode.Value == WindowMode.Fullscreen)
                    {
                        _controller.ReleaseToGame();
                        _requestNativeFullscreen = _platform.EngineIsWindowed;
                    }
                    else RequestManaged(_mode.Value, now);
                    _modeChanged = false;
                    Logger.LogInfo($"Bound game-owned UnityWndClass HWND=0x{hwnd.ToInt64():X}; engine={Screen.fullScreenMode}; startup={_mode.Value}.");
                }
                if (_modeChanged)
                {
                    _modeChanged = false;
                    if (_mode.Value == WindowMode.Fullscreen)
                    { _controller.ReleaseToGame(); _requestNativeFullscreen = _platform.EngineIsWindowed; }
                    else RequestManaged(_mode.Value, now);
                }
                // Native callbacks only enqueue actions; Unity APIs stay on Update.
                switch (_platform.TakeUiAction())
                {
                    case WindowUiAction.Borderless: RequestManaged(WindowMode.Borderless, now); break;
                    case WindowUiAction.Reset:
                        _integration.CancelPendingResolution();
                        _syncManagedOnReady = !_integration.SynchronizeManagedSelection(WindowMode.Windowed);
                        _controller.Reset(now); break;
                }
                if (now < _nextTick) return;
                _nextTick = now + 0.1f;
                // Respect a session timeout rather than silently re-enabling its retry loop.
                _controller.EnforceMode = _enforce.Value && !_controller.HasTimedOut;
                bool wasGameOwned = _controller.GameOwnsDisplay;
                _controller.Tick(now);
                if (wasGameOwned && !_controller.GameOwnsDisplay)
                    _syncManagedOnReady = !_integration.SynchronizeManagedSelection(_controller.Mode);
                if (now >= _nextUiRefresh)
                {
                    _nextUiRefresh = now + 0.25f;
                    if (_requestNativeFullscreen)
                    {
                        if (!_controller.GameOwnsDisplay) _requestNativeFullscreen = false;
                        else if (!_platform.EngineIsWindowed || _integration.TryRestoreStartupFullscreen()) _requestNativeFullscreen = false;
                    }
                    if (_syncManagedOnReady && !_controller.GameOwnsDisplay)
                        _syncManagedOnReady = !_integration.SynchronizeManagedSelection(_controller.Mode);
                    _integration.RefreshUi();
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                Logger.LogError("Window enhancement suspended for this session: " + ex);
                Cleanup(false);
            }
        }

        private void RequestManaged(WindowMode mode, double now)
        {
            _requestNativeFullscreen = false;
            _syncManagedOnReady = !_integration.SynchronizeManagedSelection(mode);
            _controller.Request(mode, now);
        }
        private void SuspendIntegration(Exception ex)
        {
            if (_failed) return;
            _failed = true;
            Logger.LogError("Display integration suspended (UNKNOWN/changed runtime binding): " + ex);
            Cleanup(true);
        }

        private void SaveWindow(WindowMode mode, WindowRect normal)
        {
            if (!_remember.Value) return;
            _saving = true;
            try
            {
                _mode.Value = mode;
                _x.Value = normal.X; _y.Value = normal.Y;
                _width.Value = normal.Width; _height.Value = normal.Height;
                Config.Save();
            }
            finally { _saving = false; }
        }
        private void OnApplicationQuit()
        { _quitting = true; try { _controller?.Save(); } catch (Exception ex) { Logger.LogWarning("Save on quit: " + ex.Message); } }
        private void OnDestroy() => Cleanup(!_quitting);
        private void Cleanup(bool restoreFrame)
        {
            try { _hooks?.Dispose(); _integration?.Dispose(); }
            catch (Exception ex) { Logger.LogWarning("Restore graphics integration: " + ex.Message); }
            _hooks = null; _integration = null;
            try { _controller?.Save(); }
            catch (Exception ex) { Logger.LogWarning("Save window: " + ex.Message); }
            try
            {
                _platform?.Dispose();
                if (restoreFrame) _platform?.RestoreOriginalFrame();
            }
            catch (Exception ex) { Logger.LogWarning("Restore window: " + ex.Message); }
            _controller = null; _platform = null;
        }
    }
}
