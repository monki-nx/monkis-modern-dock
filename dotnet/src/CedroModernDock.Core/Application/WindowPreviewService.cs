namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>Direct port of WindowPreviewService.</summary>
public class WindowPreviewService
{
    private readonly IWindowQueryGateway _windowQueryGateway;

    public WindowPreviewService(IWindowQueryGateway windowQueryGateway)
    {
        _windowQueryGateway = windowQueryGateway;
    }

    public List<WindowInfo> LoadPreview(DockProgramItemModel item) =>
        _windowQueryGateway.FindOpenWindows(item.ExecutablePath, item.Label);

    /// <summary>Open windows of a program, topmost first.</summary>
    public List<WindowInfo> FindOpenWindows(string? executablePath, string? label = null) =>
        _windowQueryGateway.FindOpenWindows(executablePath, label);

    public bool HasOpenWindows(string? executablePath, string? label = null) =>
        FindOpenWindows(executablePath, label).Count > 0;

    public List<RunningWindowInfo> FindTaskbarWindows() =>
        _windowQueryGateway.FindTaskbarWindows();

    public IReadOnlyCollection<string> AppIdsOf(string executablePath, string? label) =>
        _windowQueryGateway.AppIdsOf(executablePath, label);

    public void Activate(WindowInfo windowInfo) => _windowQueryGateway.Activate(windowInfo);

    public void Close(WindowInfo windowInfo) => _windowQueryGateway.Close(windowInfo);

    /// <summary>
    /// Asks every open window of the executable to close (WM_CLOSE, like the
    /// taskbar's "Close all windows"). Returns how many windows were asked.
    /// </summary>
    public int CloseAll(string? executablePath, string? label = null)
    {
        var windows = _windowQueryGateway.FindOpenWindows(executablePath, label);
        foreach (var window in windows)
            _windowQueryGateway.Close(window);
        return windows.Count;
    }

    /// <summary>
    /// Taskbar click on a running app (windows topmost first):
    /// <list type="bullet">
    /// <item>one window: bring it forward, or minimize it when it already is the active window;</item>
    /// <item>several windows: bring the most recent forward, or, when one of
    /// them is active, cycle to the least recent one — the activated window
    /// moves to the front of the z-order, so taking "the next one" after it
    /// only ever alternated between the two most recent windows.</item>
    /// </list>
    /// Minimized windows come back to their previous placement (snapped,
    /// maximized or floating). Returns false when there is no window.
    /// </summary>
    public bool ActivateOrMinimize(IReadOnlyList<WindowInfo> windows)
    {
        if (windows.Count == 0) return false;

        IntPtr foreground = _windowQueryGateway.GetForegroundWindow();
        int activeIndex = -1;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].Handle == foreground)
            {
                activeIndex = i;
                break;
            }
        }

        if (windows.Count == 1)
        {
            var only = windows[0];
            if (activeIndex == 0 && !_windowQueryGateway.IsMinimized(only))
                _windowQueryGateway.Minimize(only);
            else
                _windowQueryGateway.Activate(only);
            return true;
        }

        var target = windows[0];
        if (activeIndex >= 0)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (i == activeIndex) continue;
                target = windows[i];
                break;
            }
        }
        _windowQueryGateway.Activate(target);
        return true;
    }
}
