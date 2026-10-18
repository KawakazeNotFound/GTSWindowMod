// The production plugin is compiled against real Unity 2019 assemblies.
// Only the owned hidden-window smoke test uses this explicit engine shim.
namespace UnityEngine
{
    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }
    public static class Screen
    {
        public static FullScreenMode fullScreenMode = FullScreenMode.Windowed;
        public static void SetResolution(int width, int height, FullScreenMode mode) => fullScreenMode = mode;
    }
}
