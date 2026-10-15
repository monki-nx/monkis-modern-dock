namespace CedroModernDock.Core.Domain;

using CedroModernDock.Core.Application;

/// <summary>
/// Port for querying open native windows (for running-app indicators
/// and window-preview popups). The WindowInfo record is shared so the
/// application layer never depends on Win32 HWND types directly.
/// </summary>
public interface IWindowQueryGateway
{
    /// <summary>
    /// Open windows of a program, topmost first. <paramref name="label"/>
    /// helps identify apps started through an alias or a launcher.
    /// </summary>
    List<WindowInfo> FindOpenWindows(string? executablePath, string? label = null);
    List<RunningWindowInfo> FindTaskbarWindows();

    /// <summary>AppUserModelIDs the program's windows are grouped under by the taskbar.</summary>
    IReadOnlyCollection<string> AppIdsOf(string executablePath, string? label);

    void Activate(WindowInfo windowInfo);
    void Minimize(WindowInfo windowInfo);
    bool IsMinimized(WindowInfo windowInfo);
    IntPtr GetForegroundWindow();
    void Close(WindowInfo windowInfo);
}

/// <summary>Minimal info required to activate and label a window.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title);

/// <summary>Taskbar-visible window plus its owning executable path and AppUserModelID (when known).</summary>
public sealed record RunningWindowInfo(IntPtr Handle, string Title, string ExecutablePath, string? AppId = null);
