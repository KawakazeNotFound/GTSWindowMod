using GTSWindowMod.Core;

int passed = 0;
void Check(bool condition, string message)
{ if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
var work = new WindowRect(-1920, 0, 1920, 1040);
Check(WindowRect.Fit(new WindowRect(-5000, -100, 1280, 720), work).Equals(new WindowRect(-1920, 0, 1280, 720)), "negative-coordinate secondary monitor");
Check(WindowRect.Fit(new WindowRect(9999, 9999, 3000, 2000), work).Equals(work), "oversized/offscreen saved rectangle fits work area");
Check(WindowRect.Fit(new WindowRect(int.MaxValue, int.MaxValue, 800, 600), work).Equals(new WindowRect(-800, 440, 800, 600)), "extreme user coordinates do not overflow during clamp");
Check(WindowRect.Fit(new WindowRect(0, 0, 0, -1), work).Width == 1, "invalid dimensions remain positive");

var normal = new WindowRect(100, 100, 1280, 760);
var p = new FakePlatform { Bounds = normal };
var saves = new List<(WindowMode Mode, WindowRect Rect)>();
var logs = new List<string>();
WindowController Create(FakePlatform platform) => new WindowController(platform, normal, (mode, rect) => saves.Add((mode, rect)), logs.Add);
var c = Create(p);
c.Request(WindowMode.Maximized, 0);
c.Tick(0.2);
Check(p.MaximizeCalls == 0, "Unity frame transition is deferred");
c.Tick(0.5);
Check(p.MaximizeCalls == 1 && p.IsMaximized, "native maximization applied once");
c.Tick(0.6); c.Tick(1.5);
Check(saves.Last().Rect.Equals(normal) && saves.Last().Mode == WindowMode.Maximized, "maximized bounds do not replace normal restore rectangle");
p.IsMaximized = false; p.Bounds = normal;
c.Tick(2); c.Tick(3); c.Tick(4);
Check(c.Mode == WindowMode.Windowed && p.MaximizeCalls == 1, "native Restore is respected rather than re-maximized by watchdog");
p.Bounds = new WindowRect(180, 130, 1100, 700);
c.Tick(5); c.Tick(5.5);
int countBeforeStable = saves.Count;
c.Tick(6);
Check(saves.Count == countBeforeStable + 1 && saves.Last().Rect.Equals(p.Bounds), "resized window persists only after settling");
var beforeBorderless = saves.Last().Rect;
c.Request(WindowMode.Borderless, 7); c.Tick(7.5); c.Tick(7.6); c.Tick(8.5);
Check(p.Borderless && p.Bounds.Equals(p.Monitor), "borderless fills entire selected monitor");
Check(saves.Last().Rect.Equals(beforeBorderless), "borderless preserves normal-window dimensions");
c.Request(WindowMode.Windowed, 9); c.Tick(9.5);
Check(!p.Borderless && p.Bounds.Equals(beforeBorderless), "leaving borderless restores previous normal rectangle");
c.Tick(9.6); c.Tick(10.5);
p.IsMinimized = true; p.Bounds = new WindowRect(-32000, -32000, 160, 28);
c.Save();
Check(saves.Last().Rect.Equals(beforeBorderless), "minimized sentinel rectangle is never saved");
p.IsMinimized = false; p.Bounds = beforeBorderless;
c.Reset(11); c.Tick(11.5);
Check(p.Bounds.Equals(p.Default), "reset uses centered default rather than recapturing old window");

p = new FakePlatform { Bounds = normal, EngineIsWindowed = false, CompleteEngineTransition = false };
c = Create(p); c.Request(WindowMode.Windowed, 0); c.Tick(1);
Check(c.IsTransitioning && p.MoveCalls == 0, "waits for actual engine windowed acknowledgment");
c.Tick(4); c.Tick(5);
Check(!c.EnforceMode && p.PrepareCalls == 1, "failed transition disables automatic retries");
c.Save();
Check(saves.Last().Rect.Equals(normal), "save during failed transition retains good normal rectangle");

p = new FakePlatform { Bounds = normal };
c = Create(p); c.Request(WindowMode.Windowed, 0); c.Tick(0.5); c.Tick(0.6); c.Tick(1.5);
p.EngineIsWindowed = false;
p.Bounds = p.Monitor; // A real engine fullscreen reset also changes the native rectangle.
c.Tick(2);
Check(c.GameOwnsDisplay && !c.IsTransitioning && p.PrepareCalls == 1, "native fullscreen hands ownership to the game instead of forcing windowed mode");
c.Tick(2.5);
Check(!p.EngineIsWindowed && p.GameOwned && p.PrepareCalls == 1, "native fullscreen stays untouched by background maintenance");
c.Save();
Check(saves.Last().Rect.Equals(normal), "native fullscreen does not overwrite the normal restore rectangle");
c.Request(WindowMode.Windowed, 2.6); c.Tick(3);
Check(!c.GameOwnsDisplay && !p.GameOwned && p.Bounds.Equals(normal), "explicit return from native fullscreen reclaims the remembered normal window");
p.BadStyle = true;
c.Tick(3.1);
Check(!p.BadStyle, "style-only reset repaired without moving the normal window");
c.Request(WindowMode.Borderless, 4); c.Request(WindowMode.Windowed, 4.1); c.Tick(4.6);
Check(!p.Borderless && c.Mode == WindowMode.Windowed, "rapid requests apply only the latest desired mode");
p.IsSizing = true; p.Bounds = new WindowRect(300, 200, 900, 600);
int saveBeforeDrag = saves.Count;
c.Tick(6); c.Tick(7);
Check(saves.Count == saveBeforeDrag, "drag loop does not save transient sizes");
p.IsSizing = false;
c.Tick(8); c.Tick(9);
Check(saves.Last().Rect.Equals(p.Bounds), "drag completion persists settled rectangle");

p.IsValid = false;
c.Save();
Check(saves.Last().Rect.Equals(p.Bounds), "destroyed window keeps the last valid normal rectangle for rebinding");

p = new FakePlatform { Bounds = new WindowRect(2100, 100, 1280, 760) };
c = Create(p); c.Request(WindowMode.Borderless, 0); c.Tick(0.5);
Check(p.LastMonitorAnchor.Equals(normal), "startup borderless chooses the saved normal-window monitor");

p = new FakePlatform { Bounds = normal };
c = Create(p); c.Request(WindowMode.Borderless, 0);
c.ReleaseToGame();
c.Tick(0.5); c.Tick(1); c.Tick(3);
Check(c.GameOwnsDisplay && p.GameOwned && p.MoveCalls == 0, "handoff cancels pending Mod resize and waits for delayed game fullscreen entry");
p.EngineIsWindowed = false; p.Bounds = p.Monitor;
for (int i = 0; i < 100; i++) c.Tick(4 + i);
Check(c.GameOwnsDisplay && p.MoveCalls == 0, "100 maintenance ticks leave native fullscreen geometry alone");
p.EngineIsWindowed = true;
c.Tick(105); c.Tick(105.5);
Check(!c.GameOwnsDisplay && !p.GameOwned && p.Bounds.Equals(normal), "observed native fullscreen exit automatically resumes window ownership");

GameIntegrationTests.Run(Check);

if (OperatingSystem.IsWindows()) NativeSmoke.Run(Check);

Console.WriteLine($"{passed} checks passed. Logic tests and an owned hidden Win32 fixture only; no game launched or visual claims.");
