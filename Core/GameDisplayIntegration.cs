using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace GTSWindowMod.Core
{
    // Names/signatures come from the user-provided NewMainMenu/BorderlessHelper sources.
    // Runtime binding is validated before installing any hooks; no game DLL is referenced
    // or redistributed by this adapter. Tests supply explicit synthetic menu objects.
    public sealed class GameDisplayIntegration : IDisposable
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags AnyInstance = PublicInstance | BindingFlags.NonPublic;
        private readonly Func<WindowController> _controller;
        private readonly Func<double> _now;
        private readonly Action<string> _log;
        private readonly FieldInfo _instance, _graphics, _selectedMode, _controls, _submenus, _borderlessFlag;
        private readonly MethodInfo _cancelDelay;
        private readonly List<ElementSnapshot> _elements = new List<ElementSnapshot>();
        private readonly List<MemberEdit> _footerEdits = new List<MemberEdit>();
        private object _lastModeControl, _lastResolutionControl, _lastGraphicsMenu;
        public MethodInfo ApplyMethod { get; }
        public MethodInfo ConstructMethod { get; }
        public MethodInfo HelperMethod { get; }

        public GameDisplayIntegration(Func<string, Type> resolve, Func<WindowController> controller,
            Func<double> now, Action<string> log)
        {
            _controller = controller; _now = now; _log = log;
            Type menu = Required(resolve("NewMainMenu.NewMainMenu"), "NewMainMenu.NewMainMenu");
            Type globals = Required(resolve("NewMainMenu.NMM_Globals"), "NewMainMenu.NMM_Globals");
            Type helper = Required(resolve("BorderlessHelper"), "BorderlessHelper");
            Type lerp = Required(resolve("GlobalLerp"), "GlobalLerp");
            Type fps = Required(resolve("FPSBehaviour"), "FPSBehaviour");
            _instance = Required(menu.GetField("INSTANCE", BindingFlags.Static | BindingFlags.Public), "INSTANCE");
            _graphics = Required(menu.GetField("m_Graphics", AnyInstance), "m_Graphics");
            _selectedMode = Required(_graphics.FieldType.GetField("WindowMode", PublicInstance), "m_Graphics.WindowMode");
            if (!_selectedMode.FieldType.IsEnum || !HasEnum(_selectedMode.FieldType, "Windowed") ||
                !HasEnum(_selectedMode.FieldType, "Borderless") || !HasEnum(_selectedMode.FieldType, "Fullscreen"))
                throw new MissingMemberException("UNKNOWN game window-mode enum layout.");
            _controls = Required(globals.GetField("Controls", BindingFlags.Static | BindingFlags.Public), "Controls");
            _submenus = Required(globals.GetField("SubMenus", BindingFlags.Static | BindingFlags.Public), "SubMenus");
            Required(_controls.FieldType.GetField("WindowMode"), "Controls.WindowMode");
            Required(_controls.FieldType.GetField("ScreenResDB"), "Controls.ScreenResDB");
            _borderlessFlag = Required(fps.GetField("Borderless", BindingFlags.Static | BindingFlags.Public), "FPSBehaviour.Borderless");
            if (_borderlessFlag.FieldType != typeof(bool)) throw new MissingMemberException("UNKNOWN Borderless flag type.");
            _cancelDelay = Required(lerp.GetMethod("CancelDelay", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(bool) }, null), "GlobalLerp.CancelDelay(string,bool)");
            ApplyMethod = Required(menu.GetMethod("ApplyGraphicsSettings", PublicInstance, null, new[] { typeof(bool) }, null), "ApplyGraphicsSettings(bool)");
            ConstructMethod = Required(menu.GetMethod("ConstructSubMenu_Graphics", PublicInstance, null, Type.EmptyTypes, null), "ConstructSubMenu_Graphics()");
            foreach (var method in helper.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                var p = method.GetParameters();
                if (method.Name == "SetBorderlessResolution" && method.ReturnType == typeof(void) && p.Length == 5 &&
                    p[0].ParameterType == typeof(int) && p[1].ParameterType == typeof(int) && p[2].ParameterType == typeof(bool) &&
                    p[3].ParameterType.IsEnum && HasEnum(p[3].ParameterType, "ExclusiveFullScreen") && p[4].ParameterType == typeof(int))
                    HelperMethod = method;
            }
            Required(HelperMethod, "BorderlessHelper.SetBorderlessResolution(int,int,bool,FullScreenMode,int)");
        }

        private static T Required<T>(T value, string name) where T : class => value ?? throw new MissingMemberException("UNKNOWN display binding: " + name);
        private static bool HasEnum(Type type, string name) => Enum.IsDefined(type, name);
        private object Menu => _instance.GetValue(null);
        private object Graphics(object menu) => menu == null ? null : _graphics.GetValue(menu);
        private string Selection(object menu) => _selectedMode.GetValue(Required(Graphics(menu), "initialized m_Graphics")).ToString();
        public void CancelPendingResolution() => _cancelDelay.Invoke(null, new object[] { "APPLY_RESOLUTION", false });

        public void BeforeApplyGraphics(object menu)
        {
            var controller = _controller();
            if (controller == null || Graphics(menu) == null) return;
            switch (Selection(menu))
            {
                case "Fullscreen": controller.ReleaseToGame(); break;
                case "Windowed": EnsureManaged(WindowMode.Windowed); break;
                case "Borderless": EnsureManaged(WindowMode.Borderless); break;
                default: throw new MissingMemberException("UNKNOWN requested game display mode.");
            }
        }

        // Only this display helper is intercepted: quality, VSync, FOV, save and undo
        // continue through the game's original ApplyGraphicsSettings implementation.
        public bool BeforeNativeHelper(object[] args)
        {
            var controller = _controller();
            if (controller == null) return true;
            if (args == null || args.Length != 5 || !(args[2] is bool))
                throw new MissingMemberException("UNKNOWN display helper arguments.");
            bool borderless = (bool)args[2];
            string engineMode = args[3]?.ToString();
            if (engineMode != "Windowed" && engineMode != "ExclusiveFullScreen" && engineMode != "FullScreenWindow")
                throw new MissingMemberException("UNKNOWN native engine fullscreen mode: " + engineMode);
            if (!borderless && (engineMode == "ExclusiveFullScreen" || engineMode == "FullScreenWindow"))
            { controller.ReleaseToGame(); return true; }
            if (engineMode != "Windowed" && !borderless)
                throw new MissingMemberException("UNKNOWN native engine fullscreen mode: " + engineMode);
            // ApplyGraphicsSettings already routes explicit menu choices. A stale scene
            // loader/config replay must not replace the user's currently owned mode.
            if (controller.GameOwnsDisplay) EnsureManaged(borderless ? WindowMode.Borderless : WindowMode.Windowed);
            return false; // The controller owns this resize, including delayed window-style work.
        }

        private void EnsureManaged(WindowMode requested)
        {
            var c = _controller();
            WindowMode actual = c.Mode == WindowMode.Maximized ? WindowMode.Windowed : c.Mode;
            // Applying quality while natively maximized must not restore the window.
            if (c.GameOwnsDisplay || actual != requested)
            { CancelPendingResolution(); c.Request(requested, _now()); }
        }

        public bool SynchronizeManagedSelection(WindowMode mode)
        {
            if (mode == WindowMode.Fullscreen) return false;
            object menu = Menu, graphics = Graphics(menu);
            if (graphics == null) return false;
            CancelPendingResolution();
            string name = mode == WindowMode.Borderless ? "Borderless" : "Windowed";
            _selectedMode.SetValue(graphics, Enum.Parse(_selectedMode.FieldType, name));
            _borderlessFlag.SetValue(null, mode == WindowMode.Borderless);
            object controls = _controls.GetValue(null);
            object element = Field(controls, "WindowMode");
            if (element != null)
            {
                var set = Required(element.GetType().GetMethod("SetValue", PublicInstance, null, new[] { typeof(object) }, null), "WindowMode.SetValue(object)");
                set.Invoke(element, new object[] { Convert.ToInt32(_selectedMode.GetValue(graphics)) });
            }
            return true;
        }

        public bool TryRestoreStartupFullscreen()
        {
            object menu = Menu, graphics = Graphics(menu);
            if (graphics == null) return false;
            // Start() fills these arrays and indices over several steps; wait for a
            // fully initialized native fullscreen choice, then invoke the ORIGINAL API.
            var resolutions = Field(graphics, "ScreenResolutionsFS") as Array;
            object indexValue = Field(graphics, "CurrentResolutionFS");
            if (resolutions == null || !(indexValue is int) || (int)indexValue < 0 || (int)indexValue >= resolutions.Length) return false;
            _selectedMode.SetValue(graphics, Enum.Parse(_selectedMode.FieldType, "Fullscreen"));
            _borderlessFlag.SetValue(null, false);
            ApplyMethod.Invoke(menu, new object[] { false });
            return true;
        }

        public void RefreshUi()
        {
            var controller = _controller();
            object menu = Menu;
            if (controller == null || Graphics(menu) == null) return;
            object controls = _controls.GetValue(null);
            object modeControl = Field(controls, "WindowMode"), resolution = Field(controls, "ScreenResDB");
            object graphicsMenu = Field(_submenus.GetValue(null), "Graphics");
            if (!ReferenceEquals(graphicsMenu, _lastGraphicsMenu) && !controller.GameOwnsDisplay)
                SynchronizeManagedSelection(controller.Mode);
            if (!ReferenceEquals(modeControl, _lastModeControl) || !ReferenceEquals(resolution, _lastResolutionControl) || !ReferenceEquals(graphicsMenu, _lastGraphicsMenu))
            {
                RestoreUi();
                _lastModeControl = modeControl; _lastResolutionControl = resolution; _lastGraphicsMenu = graphicsMenu;
                if (modeControl != null) _elements.Add(new ElementSnapshot(modeControl));
                if (resolution != null) _elements.Add(new ElementSnapshot(resolution));
                CaptureTryResolutionButton(graphicsMenu);
            }
            bool previewNative = Selection(menu) == "Fullscreen";
            foreach (var snapshot in _elements)
            {
                bool isMode = ReferenceEquals(snapshot.Target, modeControl);
                snapshot.Apply(isMode ? false : !previewNative, isMode
                    ? snapshot.OriginalLabel + (previewNative ? " [Fullscreen: Game]" : " [Mod managed]")
                    : snapshot.OriginalLabel + (previewNative ? "" : " [Mod managed]"), isMode
                    ? "Fullscreen: game native fullscreen. Other modes: GTSWindowMod. 全屏由游戏管理，其余模式由 Mod 接管。"
                    : previewNative ? snapshot.OriginalTooltip : "由 GTSWindowMod 接管。拖动窗口边框调整大小，或使用原生最大化。Managed by GTSWindowMod.");
            }
            foreach (var edit in _footerEdits) edit.Apply(!previewNative);
        }

        private void CaptureTryResolutionButton(object graphicsMenu)
        {
            var buttons = Field(graphicsMenu, "BigButtons") as IEnumerable;
            if (buttons == null) return;
            foreach (object button in buttons)
            {
                // Exact label from ConstructSubMenu_Graphics, not a fuzzy UI-name match.
                if (!string.Equals(Field(button, "Label") as string, "Try Resolution", StringComparison.Ordinal)) continue;
                AddEdit(Field(button, "Ref_CanvasGroup"), "interactable", false);
                object group = Field(button, "Ref_CanvasGroup");
                if (group != null) AddEdit(group, "alpha", Convert.ToSingle(Property(group, "alpha")) * 0.25f);
                AddEdit(Field(button, "Ref_Trigger"), "enabled", false);
                AddEdit(Field(button, "Ref_Label"), "text", "Try Resolution [Mod managed]");
            }
        }
        private void AddEdit(object target, string member, object disabledValue)
        { if (target != null) _footerEdits.Add(new MemberEdit(target, member, disabledValue)); }
        private static object Field(object target, string name) => target?.GetType().GetField(name, AnyInstance)?.GetValue(target);
        private static object Property(object target, string name) => Required(target.GetType().GetProperty(name, PublicInstance), name).GetValue(target, null);

        public void RestoreUi()
        {
            foreach (var e in _elements) { try { e.Restore(); } catch (Exception ex) { _log("UI restore skipped for an expired element: " + ex.GetBaseException().Message); } }
            foreach (var e in _footerEdits) { try { e.Apply(false); } catch { /* Previous scene already destroyed its UI. */ } }
            _elements.Clear(); _footerEdits.Clear();
            _lastModeControl = null; _lastResolutionControl = null; _lastGraphicsMenu = null;
        }
        public void Dispose() => RestoreUi();

        private sealed class ElementSnapshot
        {
            public readonly object Target;
            public readonly string OriginalLabel, OriginalTooltip;
            private readonly bool _disabled;
            private readonly PropertyInfo _label, _isDisabled;
            private readonly MethodInfo _tooltip;
            private readonly FieldInfo _tooltipField;
            private readonly float _hoverTime, _closeTime;
            private string _lastTooltip;
            public ElementSnapshot(object target)
            {
                Target = target;
                Type t = target.GetType();
                _label = Required(t.GetProperty("label", PublicInstance), "element.label");
                _isDisabled = Required(t.GetProperty("isDisabled", PublicInstance), "element.isDisabled");
                _tooltip = Required(t.GetMethod("SetTooltipText", PublicInstance, null,
                    new[] { typeof(string), typeof(float), typeof(float) }, null), "SetTooltipText");
                OriginalLabel = (string)_label.GetValue(target, null);
                _disabled = (bool)_isDisabled.GetValue(target, null);
                for (Type current = t; current != null; current = current.BaseType)
                {
                    var field = current.GetField("m_TooltipText", AnyInstance | BindingFlags.DeclaredOnly);
                    if (field != null) { _tooltipField = field; OriginalTooltip = field.GetValue(target) as string; break; }
                }
                _lastTooltip = OriginalTooltip;
                _hoverTime = ReadTiming(t, target, "m_TooltipHoverTime", 1f);
                _closeTime = ReadTiming(t, target, "m_TooltipCloseTime", 3f);
            }
            public void Apply(bool disabled, string label, string tooltip)
            {
                bool value = _disabled || disabled;
                if ((bool)_isDisabled.GetValue(Target, null) != value) _isDisabled.SetValue(Target, value, null);
                if (!Equals(_label.GetValue(Target, null), label)) _label.SetValue(Target, label, null);
                object liveTooltip = _tooltipField != null ? _tooltipField.GetValue(Target) : _lastTooltip;
                if (!Equals(liveTooltip, tooltip))
                { _tooltip.Invoke(Target, new object[] { tooltip, _hoverTime, _closeTime }); _lastTooltip = tooltip; }
            }
            private static float ReadTiming(Type t, object target, string name, float fallback)
            {
                for (Type current = t; current != null; current = current.BaseType)
                {
                    var field = current.GetField(name, AnyInstance | BindingFlags.DeclaredOnly);
                    if (field != null && field.FieldType == typeof(float)) return (float)field.GetValue(target);
                }
                return fallback;
            }
            public void Restore() => Apply(_disabled, OriginalLabel, OriginalTooltip);
        }
        private sealed class MemberEdit
        {
            private readonly object _target, _original, _disabled;
            private readonly PropertyInfo _property;
            public MemberEdit(object target, string property, object disabled)
            {
                _target = target; _disabled = disabled;
                _property = Required(target.GetType().GetProperty(property, PublicInstance), property);
                _original = _property.GetValue(target, null);
            }
            public void Apply(bool disabled)
            {
                object value = disabled ? _disabled : _original;
                if (!Equals(_property.GetValue(_target, null), value)) _property.SetValue(_target, value, null);
            }
        }
    }
}
