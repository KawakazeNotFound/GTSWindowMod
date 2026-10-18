using GTSWindowMod;
using GTSWindowMod.Core;
using F = GameIntegrationTests;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string text)
    { if (!ok) throw new Exception(text); _checks++; Console.WriteLine("PASS " + text); }
    private static void Main()
    {
        try { Run(); }
        catch (Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                Console.Error.WriteLine(current.GetType().FullName);
                Console.Error.WriteLine(current.Message);
            }
            Environment.ExitCode = 1;
        }
    }
    private static void Run()
    {
        var normal = new WindowRect(100, 100, 1280, 760);
        var platform = new FakePlatform { Bounds = normal };
        var c = new WindowController(platform, normal, (m, r) => { }, _ => { });
        c.Request(WindowMode.Maximized, 0); c.Tick(0.5);
        var menu = F.TestMenu.INSTANCE = new F.TestMenu();
        F.TestGlobals.Controls = new F.TestControls(); F.TestGlobals.SubMenus = new F.TestSubmenus();
        F.TestMenu.BeforeApply = null; F.TestMenu.HelperFilter = null;
        F.TestHelper.Calls = 0;
        Type Resolve(string name) => name switch
        {
            "NewMainMenu.NewMainMenu" => typeof(F.TestMenu),
            "NewMainMenu.NMM_Globals" => typeof(F.TestGlobals),
            "BorderlessHelper" => typeof(F.TestHelper),
            "GlobalLerp" => typeof(F.TestLerp),
            "FPSBehaviour" => typeof(F.TestFps),
            _ => null
        };
        using var integration = new GameDisplayIntegration(Resolve, () => c, () => 2, Console.WriteLine);
        Exception failure = null;
        using (var hooks = new GameDisplayHooks(integration, ex =>
        { failure = ex; Console.Error.WriteLine("Hook error: " + ex.GetType().Name + ": " + ex.Message); }))
        {
            menu.ConstructSubMenu_Graphics();
            Check(F.TestGlobals.Controls.ScreenResDB.isDisabled, "real Harmony menu postfix annotates synthetic resolution control");
            menu.ApplyGraphicsSettings(false);
            Check(menu.QualityApplies == 1 && menu.VsyncApplies == 1 && F.TestHelper.Calls == 0,
                "real helper prefix blocks only display changes while original graphics method runs");
            Check(c.Mode == WindowMode.Maximized, "real Apply prefix preserves Mod maximization for quality-only apply");
            menu.Graphics.WindowMode = F.TestWindowMode.Fullscreen;
            menu.ApplyGraphicsSettings(false);
            Check(c.GameOwnsDisplay && F.TestHelper.Calls == 1 && !F.TestGlobals.Controls.ScreenResDB.isDisabled,
                "real Harmony prefixes allow native fullscreen and postfix restores native resolution UI");
            menu.Graphics.WindowMode = F.TestWindowMode.Windowed;
            menu.ApplyGraphicsSettings(false);
            c.Tick(3);
            Check(!c.GameOwnsDisplay && F.TestHelper.Calls == 1 && F.TestGlobals.Controls.ScreenResDB.isDisabled,
                "real hooked fullscreen exit reclaims window handling without calling game window helper");
            Check(failure == null, "all three production hook entrypoints execute without a callback failure");
        }
        menu.ApplyGraphicsSettings(false);
        Check(F.TestHelper.Calls == 2, "disposing production hooks restores the original helper call path");
        integration.Dispose();
        Check(!F.TestGlobals.Controls.ScreenResDB.isDisabled, "disposing integration restores original menu state");
        Console.WriteLine($"{_checks} real-Harmony checks passed on synthetic .NET Framework fixtures; not Unity runtime evidence.");
    }
}

// The hook class uses only the plugin GUID, not the actual Unity plugin entrypoint.
namespace GTSWindowMod { internal static class Plugin { public const string Guid = "com.giantess.windowmod.synthetic-tests"; } }
