using GTSWindowMod.Core;

// Explicit synthetic menu fixture. It mirrors only the interface shape needed by
// the adapter; none of the game's implementation or assets is included here.
static class GameIntegrationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var normal = new WindowRect(100, 100, 1280, 760);
        var platform = new FakePlatform { Bounds = normal };
        var saved = new List<(WindowMode Mode, WindowRect Rect)>();
        var controller = new WindowController(platform, normal, (m, r) => saved.Add((m, r)), _ => { });
        double now = 0;
        controller.Request(WindowMode.Maximized, now); controller.Tick(now = 0.5);
        var menu = TestMenu.INSTANCE = new TestMenu();
        TestGlobals.Controls = new TestControls(); TestGlobals.SubMenus = new TestSubmenus();
        TestLerp.Calls = 0; TestLerp.Pending = true;
        TestHelper.Calls = 0;
        Type Resolve(string name) => name switch
        {
            "NewMainMenu.NewMainMenu" => typeof(TestMenu),
            "NewMainMenu.NMM_Globals" => typeof(TestGlobals),
            "BorderlessHelper" => typeof(TestHelper),
            "GlobalLerp" => typeof(TestLerp),
            "FPSBehaviour" => typeof(TestFps),
            _ => null
        };
        using var bridge = new GameDisplayIntegration(Resolve, () => controller, () => now, _ => { });
        TestMenu.BeforeApply = bridge.BeforeApplyGraphics;
        TestMenu.HelperFilter = bridge.BeforeNativeHelper;
        try
        {
            bridge.RefreshUi();
            var controls = TestGlobals.Controls;
            var buttons = TestGlobals.SubMenus.Graphics.BigButtons;
            check(!controls.WindowMode.isDisabled && controls.WindowMode.label.Contains("Mod managed"), "mode dropdown remains usable and identifies Mod-managed modes");
            check(controls.ScreenResDB.isDisabled && controls.ScreenResDB.label.Contains("Mod managed"), "managed resolution control is greyed and labeled");
            check(controls.ScreenResDB.Tooltip.Contains("GTSWindowMod"), "managed resolution tooltip explains ownership");
            check(!buttons[2].Ref_CanvasGroup.interactable && !buttons[2].Ref_Trigger.enabled && buttons[2].Ref_CanvasGroup.alpha == 0.2f,
                "Try Resolution is greyed and its pointer trigger is disabled while Mod owns resolution");
            check(buttons[0].Ref_Trigger.enabled && buttons[1].Ref_Trigger.enabled && controls.VSync.enabled,
                "Cancel, Apply & Save and unrelated VSync UI remain enabled");
            int prepareBeforeQuality = platform.PrepareCalls;
            menu.ApplyGraphicsSettings(false);
            check(menu.QualityApplies == 1 && menu.VsyncApplies == 1 && TestHelper.Calls == 0,
                "original quality/VSync application executes while its stale window-resolution branch is intercepted");
            check(controller.Mode == WindowMode.Maximized && platform.PrepareCalls == prepareBeforeQuality,
                "applying graphics quality preserves the native maximized state");

            menu.Graphics.WindowMode = TestWindowMode.Fullscreen;
            bridge.RefreshUi();
            check(!controls.ScreenResDB.isDisabled && controls.ScreenResDB.label == "Window Resolution", "selecting Fullscreen restores the native resolution picker before Apply");
            check(buttons[2].Ref_Trigger.enabled && buttons[2].Ref_CanvasGroup.alpha == 0.8f, "Fullscreen preview restores original Try Resolution button state");
            check(controls.WindowMode.label.Contains("Fullscreen: Game"), "Fullscreen selection is explicitly marked as game-owned");
            check(!controller.GameOwnsDisplay, "previewing Fullscreen does not take ownership until the game applies it");
            menu.Graphics.WindowMode = TestWindowMode.Windowed;
            bridge.BeforeApplyGraphics(menu); bridge.RefreshUi();
            check(controller.Mode == WindowMode.Maximized && controls.ScreenResDB.isDisabled,
                "cancelling a fullscreen preview preserves Mod maximization and restores resolution takeover UI");
            menu.Graphics.WindowMode = TestWindowMode.Fullscreen; bridge.RefreshUi();
            menu.ApplyGraphicsSettings(false);
            check(controller.GameOwnsDisplay && platform.GameOwned && TestHelper.Calls == 1,
                "applying Fullscreen releases ownership and runs the original native fullscreen helper");
            check(TestHelper.LastMode == TestEngineMode.ExclusiveFullScreen, "native fullscreen uses the game's exclusive-fullscreen argument unchanged");
            controller.Tick(now = 1);
            check(controller.GameOwnsDisplay && platform.PrepareCalls == prepareBeforeQuality,
                "Mod waits through the game's asynchronous fullscreen delay without forcing windowed mode");
            platform.EngineIsWindowed = false; platform.Bounds = platform.Monitor;
            controller.Tick(now = 1.5); controller.Save();
            check(saved.Last().Rect.Equals(normal), "game-owned fullscreen retains normal-window restore dimensions");
            menu.ApplyGraphicsSettings(false);
            check(TestHelper.Calls == 2 && menu.QualityApplies == 3, "native fullscreen resolution and quality can be applied repeatedly without Mod interception");

            menu.Graphics.WindowMode = TestWindowMode.Borderless;
            bridge.RefreshUi();
            check(controls.ScreenResDB.isDisabled, "previewing return to Borderless marks resolution as Mod-managed again");
            menu.ApplyGraphicsSettings(false);
            controller.Tick(now = 2);
            check(!controller.GameOwnsDisplay && !platform.GameOwned && platform.Borderless && TestHelper.Calls == 2,
                "applying Borderless from native fullscreen reclaims through Mod and skips game delayed style writes");
            check(!TestLerp.Pending && TestLerp.LastName == "APPLY_RESOLUTION" && !TestLerp.LastFinish,
                "reclaim cancels only the named stale resolution delay without executing its finish action");
            bridge.BeforeNativeHelper(new object[] { 800, 600, false, TestEngineMode.Windowed, 60 });
            check(controller.Mode == WindowMode.Borderless,
                "stale scene/config window helper calls do not replace the Mod-owned mode");

            bridge.SynchronizeManagedSelection(WindowMode.Windowed);
            controller.Request(WindowMode.Windowed, now); controller.Tick(now = 2.5);
            check(menu.Graphics.WindowMode == TestWindowMode.Windowed && !TestFps.Borderless && controls.WindowMode.LastValue == 0,
                "mouse-only Mod mode changes synchronize native model, flag and dropdown without firing a callback");
            bridge.BeforeNativeHelper(new object[] { 1920, 1080, false, TestEngineMode.ExclusiveFullScreen, 144 });
            check(controller.GameOwnsDisplay, "direct native fullscreen helper requests also release ownership");
            bool allowed = bridge.BeforeNativeHelper(new object[] { 800, 600, false, TestEngineMode.Windowed, 60 });
            controller.Tick(now = 3);
            check(!allowed && !controller.GameOwnsDisplay && platform.Bounds.Equals(normal), "direct native return-to-windowed request reclaims the remembered window");

            var oldResolution = controls.ScreenResDB;
            TestGlobals.Controls = new TestControls();
            bridge.RefreshUi();
            check(!oldResolution.isDisabled && oldResolution.label == "Window Resolution", "reconstructed menu releases its previous control snapshots");
            check(TestGlobals.Controls.ScreenResDB.isDisabled, "reconstructed resolution control receives the same takeover annotation");
            bridge.Dispose();
            check(!TestGlobals.Controls.ScreenResDB.isDisabled && TestGlobals.Controls.WindowMode.label == "Window Mode", "adapter disposal restores original labels and interactability");
            check(buttons[2].Ref_Trigger.enabled && buttons[2].Ref_Label.text == "Try Resolution", "adapter disposal restores native footer label and click handling");

            menu.Graphics.CurrentResolutionFS = -1;
            int oldApplyCount = menu.QualityApplies;
            check(!bridge.TryRestoreStartupFullscreen() && menu.QualityApplies == oldApplyCount, "startup fullscreen waits for the game's initialized resolution index");
            menu.Graphics.CurrentResolutionFS = 0;
            check(bridge.TryRestoreStartupFullscreen() && menu.QualityApplies == oldApplyCount + 1 && controller.GameOwnsDisplay,
                "remembered fullscreen enters by invoking the original game ApplyGraphicsSettings API");
            bool rejected = false;
            try { bridge.BeforeNativeHelper(new object[] { 1, 1, true, (TestEngineMode)999, 60 }); }
            catch (MissingMemberException) { rejected = true; }
            check(rejected, "unknown fullscreen values are reported instead of guessing a mode");
            rejected = false;
            try { using var missing = new GameDisplayIntegration(_ => null, () => controller, () => now, _ => { }); }
            catch (MissingMemberException) { rejected = true; }
            check(rejected, "missing runtime bindings fail explicitly before installing display hooks");
        }
        finally { TestMenu.BeforeApply = null; TestMenu.HelperFilter = null; }
    }

    public enum TestWindowMode { Windowed, Borderless, Fullscreen }
    public enum TestEngineMode { Windowed, ExclusiveFullScreen, FullScreenWindow }
    public sealed class TestGraphics
    {
        public TestWindowMode WindowMode;
        public int CurrentResolutionFS = 0;
        public int[] ScreenResolutionsFS = { 1 };
    }
    public sealed class TestMenu
    {
        public static TestMenu INSTANCE;
        public static Action<object> BeforeApply;
        public static Func<object[], bool> HelperFilter;
        private readonly TestGraphics m_Graphics = new TestGraphics();
        public TestGraphics Graphics => m_Graphics;
        public int QualityApplies, VsyncApplies;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void ConstructSubMenu_Graphics() { }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void ApplyGraphicsSettings(bool showTimeout)
        {
            BeforeApply?.Invoke(this);
            QualityApplies++; VsyncApplies++;
            bool borderless = m_Graphics.WindowMode == TestWindowMode.Borderless;
            var mode = m_Graphics.WindowMode == TestWindowMode.Fullscreen ? TestEngineMode.ExclusiveFullScreen : TestEngineMode.Windowed;
            var args = new object[] { 1920, 1080, borderless, mode, 144 };
            if (HelperFilter == null || HelperFilter(args)) TestHelper.SetBorderlessResolution(1920, 1080, borderless, mode, 144);
        }
    }
    public static class TestHelper
    {
        public static int Calls;
        public static TestEngineMode LastMode;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void SetBorderlessResolution(int w, int h, bool borderless, TestEngineMode mode, int hz)
        { Calls++; LastMode = mode; TestLerp.Pending = true; }
    }
    public static class TestLerp
    {
        public static int Calls;
        public static bool Pending, LastFinish;
        public static string LastName;
        public static void CancelDelay(string name, bool finish)
        { Calls++; Pending = false; LastName = name; LastFinish = finish; }
    }
    public static class TestFps { public static bool Borderless = false; }
    public static class TestGlobals
    { public static TestControls Controls; public static TestSubmenus SubMenus; }
    public sealed class TestControls
    {
        public TestElement WindowMode = new TestElement("Window Mode");
        public TestElement ScreenResDB = new TestElement("Window Resolution");
        public TestTrigger VSync = new TestTrigger();
    }
    public sealed class TestSubmenus { public TestGraphicsMenu Graphics = new TestGraphicsMenu(); }
    public sealed class TestGraphicsMenu
    {
        public List<TestButton> BigButtons = new List<TestButton>
        { new TestButton("Cancel"), new TestButton("Apply & Save"), new TestButton("Try Resolution") };
    }
    public sealed class TestButton
    {
        public string Label;
        public TestGroup Ref_CanvasGroup = new TestGroup();
        public TestTrigger Ref_Trigger = new TestTrigger();
        public TestText Ref_Label;
        public TestButton(string label) { Label = label; Ref_Label = new TestText { text = label }; }
    }
    public sealed class TestGroup { public bool interactable { get; set; } = true; public float alpha { get; set; } = 0.8f; }
    public sealed class TestTrigger { public bool enabled { get; set; } = true; }
    public sealed class TestText { public string text { get; set; } }
    public sealed class TestElement
    {
        private string m_TooltipText = "Original tooltip";
        private float m_TooltipHoverTime = 0.4f, m_TooltipCloseTime = 8f;
        public string label { get; set; }
        public bool isDisabled { get; set; }
        public string Tooltip => m_TooltipText;
        public int LastValue;
        public TestElement(string text) { label = text; }
        public TestElement SetTooltipText(string text, float hover, float close)
        { m_TooltipText = text; m_TooltipHoverTime = hover; m_TooltipCloseTime = close; return this; }
        public TestElement SetValue(object value) { LastValue = (int)value; return this; }
    }
}
