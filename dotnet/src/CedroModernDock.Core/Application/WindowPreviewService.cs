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
        _windowQueryGateway.FindOpenWindows(item.ExecutablePath);

    public bool HasOpenWindows(string? executablePath) =>
        _windowQueryGateway.FindOpenWindows(executablePath).Count > 0;

    public List<RunningWindowInfo> FindTaskbarWindows() =>
        _windowQueryGateway.FindTaskbarWindows();

    public void Activate(WindowInfo windowInfo) => _windowQueryGateway.Activate(windowInfo);

    public void Close(WindowInfo windowInfo) => _windowQueryGateway.Close(windowInfo);

    /// <summary>
    /// Asks every open window of the executable to close (WM_CLOSE, like the
    /// taskbar's "Close all windows"). Returns how many windows were asked.
    /// </summary>
    public int CloseAll(string? executablePath)
    {
        var windows = _windowQueryGateway.FindOpenWindows(executablePath);
        foreach (var window in windows)
            _windowQueryGateway.Close(window);
        return windows.Count;
    }
}
