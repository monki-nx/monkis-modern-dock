namespace CedroModernDock.Infrastructure.Windows.Adapters;

using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Adapter wrapping Win32WindowQuery to implement IWindowQueryGateway.
/// Direct port of JnaWindowQueryGateway.java. Windows are matched to dock
/// items by executable path and, like the taskbar, by AppUserModelID.
/// </summary>
public class Win32WindowQueryGateway : IWindowQueryGateway
{
    private readonly AppUserModelIdResolver _appIds;

    public Win32WindowQueryGateway(AppUserModelIdResolver? appIds = null)
    {
        _appIds = appIds ?? new AppUserModelIdResolver();
    }

    public List<WindowInfo> FindOpenWindows(string? executablePath, string? label = null)
    {
        if (string.IsNullOrEmpty(executablePath))
            return new List<WindowInfo>();
        var windows = Win32WindowQuery.GetOpenWindows(executablePath,
            _appIds.ItemAppIds(executablePath, label), _appIds.WindowAppId);
        return windows.Select(w => new WindowInfo(w.Handle, w.Title)).ToList();
    }

    public List<RunningWindowInfo> FindTaskbarWindows()
    {
        return Win32WindowQuery.GetTaskbarWindows(_appIds.WindowAppId, _appIds.ExecutableOf)
            .Select(w => new RunningWindowInfo(w.Handle, w.Title, w.ExecutablePath, w.AppId))
            .ToList();
    }

    public IReadOnlyCollection<string> AppIdsOf(string executablePath, string? label) =>
        _appIds.ItemAppIds(executablePath, label);

    public void Activate(WindowInfo windowInfo)
    {
        Win32WindowQuery.ActivateWindow(windowInfo.Handle);
    }

    public void Minimize(WindowInfo windowInfo)
    {
        Win32WindowQuery.MinimizeWindow(windowInfo.Handle);
    }

    public bool IsMinimized(WindowInfo windowInfo) => Win32WindowQuery.IsMinimized(windowInfo.Handle);

    public IntPtr GetForegroundWindow() => User32.GetForegroundWindow();

    public void Close(WindowInfo windowInfo)
    {
        Win32WindowQuery.CloseWindow(windowInfo.Handle);
    }
}
