using GTSWindowMod.Core;

sealed class FakePlatform : IWindowPlatform
{
    public bool IsValid { get; set; } = true;
    public bool IsMinimized { get; set; }
    public bool IsMaximized { get; set; }
    public bool IsSizing { get; set; }
    public bool EngineIsWindowed { get; set; } = true;
    public bool CompleteEngineTransition = true, Borderless, BadStyle, GameOwned;
    public int MaximizeCalls, MoveCalls, PrepareCalls;
    public WindowRect Bounds { get; set; }
    public WindowRect Work = new WindowRect(0, 0, 1920, 1040);
    public WindowRect Monitor = new WindowRect(0, 0, 1920, 1080);
    public WindowRect Default = new WindowRect(320, 140, 1280, 760), LastMonitorAnchor;
    public WindowRect WorkAreaFor(WindowRect anchor) => Work;
    public WindowRect MonitorAreaFor(WindowRect anchor) { LastMonitorAnchor = anchor; return Monitor; }
    public WindowRect DefaultWindow(WindowRect anchor) => Default;
    public void PrepareEngine(WindowRect target, bool borderless) { PrepareCalls++; if (CompleteEngineTransition) EngineIsWindowed = true; }
    public void SetBorderless(bool value) { Borderless = value; BadStyle = false; }
    public bool HasExpectedStyle(bool borderless) => Borderless == borderless && !BadStyle;
    public void Restore() { IsMaximized = false; }
    public void Move(WindowRect rect) { Bounds = rect; MoveCalls++; }
    public void Maximize() { IsMaximized = true; Bounds = Work; MaximizeCalls++; }
    public void SetGameOwned(bool value) { GameOwned = value; }
}
