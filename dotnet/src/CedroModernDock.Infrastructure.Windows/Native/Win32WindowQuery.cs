using System.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Direct port of the original Java NativeWindowUtils.
/// Enumerates open top-level windows matching a given executable path,
/// and activates (restores + foregrounds) a specific window.
/// </summary>
public static class Win32WindowQuery
{
    /// <summary>Minimal info required to activate and label a window.</summary>
    public sealed record WindowInfo(IntPtr Handle, string Title);

    /// <summary>Taskbar-visible window with its owning executable path and AppUserModelID (when known).</summary>
    public sealed record RunningWindowInfo(IntPtr Handle, string Title, string ExecutablePath, string? AppId = null);

    // For UWP/AppX apps the visible top-level window is an ApplicationFrameHost
    // frame whose real app exe is found in a child CoreWindow. When the app is
    // minimized Windows reparents that child away, so the lookup fails and the
    // entry would flip to ApplicationFrameHost.exe (losing its label and icon).
    // Cache the resolved app exe per frame HWND so it stays stable across
    // minimize/restore; entries are pruned when the frame window goes away.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, string> _frameAppExeCache = new();

    /// <summary>
    /// Enumerates all visible top-level windows that would appear in the Windows
    /// taskbar, returning each with its owning executable path. Mirrors the
    /// taskbar's own filter: visible, titled, not a tool window, and either an
    /// app window or unowned. Excludes cloaked (Win+D hidden) windows.
    /// </summary>
    public static List<RunningWindowInfo> GetTaskbarWindows(Func<IntPtr, string?>? windowAppId = null)
    {
        var windows = new List<RunningWindowInfo>();

        // Prune cached UWP frame→app mappings for frames that no longer exist.
        foreach (var handle in _frameAppExeCache.Keys.ToList())
        {
            if (!User32.IsWindow(handle))
                _frameAppExeCache.TryRemove(handle, out _);
        }

        User32.EnumWindows((hWnd, _) =>
        {
            if (TryGetTaskbarWindow(hWnd, out var info))
                windows.Add(windowAppId == null ? info! : info! with { AppId = windowAppId(hWnd) });
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static bool TryGetTaskbarWindow(IntPtr hWnd, out RunningWindowInfo? info)
    {
        info = null;

        if (!User32.IsWindowVisible(hWnd))
            return false;

        // Skip cloaked windows (hidden by Win+D / minimized to taskbar).
        if (DwmThumbnailInterop.IsCloaked(hWnd))
            return false;

        // Skip the desktop window ("Program Manager", class Progman) and other
        // explorer-owned shell surfaces that never appear in the real taskbar.
        if (IsProgman(hWnd))
            return false;

        int exStyle = User32.GetWindowLongPtr(hWnd, Win32Constants.GWL_EXSTYLE).ToInt32();
        if ((exStyle & Win32Constants.WS_EX_TOOLWINDOW) != 0)
            return false;

        // Taskbar apps: either explicitly an app window or an unowned window.
        IntPtr owner = User32.GetWindow(hWnd, Win32Constants.GW_OWNER);
        if ((exStyle & Win32Constants.WS_EX_APPWINDOW) == 0 && owner != IntPtr.Zero)
            return false;

        var titleBuilder = new StringBuilder(1024);
        User32.GetWindowText(hWnd, titleBuilder, 1024);
        string title = titleBuilder.ToString().Trim();
        if (string.IsNullOrEmpty(title))
            return false;

        string? executablePath = GetProcessPath(hWnd);
        if (string.IsNullOrEmpty(executablePath))
            return false;

        // UWP/AppX apps: the visible window belongs to ApplicationFrameHost,
        // but the actual app runs in a child CoreWindow. Use the child's
        // executable so the dock groups, labels and icons it as the real app.
        if (string.Equals(System.IO.Path.GetFileName(executablePath),
                "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            string? appPath = GetChildProcessExePath(hWnd, executablePath);
            if (!string.IsNullOrEmpty(appPath))
            {
                _frameAppExeCache[hWnd] = appPath;
                executablePath = appPath;
            }
            else if (_frameAppExeCache.TryGetValue(hWnd, out string? cached))
            {
                // Minimized frames lose their CoreWindow child, so fall back to
                // the previously resolved app to keep label/icon stable.
                executablePath = cached;
            }
        }

        info = new RunningWindowInfo(hWnd, title, executablePath);
        return true;
    }

    /// <summary>
    /// Finds the first child window running a different executable than the
    /// frame host — for UWP apps that is the CoreWindow hosting the app UI.
    /// </summary>
    private static string? GetChildProcessExePath(IntPtr hWnd, string framePath)
    {
        string? result = null;
        User32.EnumChildWindows(hWnd, (child, _) =>
        {
            string? path = GetProcessPath(child);
            if (!string.IsNullOrEmpty(path) &&
                !string.Equals(path, framePath, StringComparison.OrdinalIgnoreCase))
            {
                result = path;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static string? GetProcessPath(IntPtr hWnd)
    {
        User32.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return null;

        IntPtr process = Kernel32.OpenProcess(
            Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;

        try
        {
            var pathBuffer = new StringBuilder(1024);
            uint size = (uint)pathBuffer.Capacity;
            if (Kernel32.QueryFullProcessImageName(process, 0, pathBuffer, ref size))
                return NormalizePath(pathBuffer.ToString(0, (int)size));
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    /// <summary>
    /// Returns all visible top-level windows (in z-order, topmost first) whose
    /// owning process image path matches <paramref name="executablePath"/> or
    /// whose AppUserModelID is one of <paramref name="appIds"/> — the taskbar
    /// groups windows by AppUserModelID, which is how an execution alias
    /// (wt.exe) or a launcher finds the windows of the app it starts.
    /// </summary>
    public static List<WindowInfo> GetOpenWindows(string? executablePath,
        IReadOnlyCollection<string>? appIds = null, Func<IntPtr, string?>? windowAppId = null)
    {
        var windows = new List<WindowInfo>();
        if (string.IsNullOrEmpty(executablePath))
            return windows;

        var targetPath = NormalizePath(executablePath);
        bool matchByAppId = appIds is { Count: > 0 } && windowAppId != null;

        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            var titleBuilder = new StringBuilder(1024);
            User32.GetWindowText(hWnd, titleBuilder, 1024);
            string title = titleBuilder.ToString().Trim();

            // Skip hidden/invisible windows that report visible but have empty titles.
            if (string.IsNullOrEmpty(title))
                return true;

            // Skip the desktop window ("Program Manager", class Progman): it belongs
            // to explorer.exe but must never be listed as an open window.
            if (IsProgman(hWnd))
                return true;

            // Skip tool windows: they never appear in the taskbar, and the dock's
            // own windows (the dock bar and the preview popup) are tool windows.
            int exStyle = User32.GetWindowLongPtr(hWnd, Win32Constants.GWL_EXSTYLE).ToInt32();
            if ((exStyle & Win32Constants.WS_EX_TOOLWINDOW) != 0)
                return true;

            // Skip IME helper windows ("Default IME" / "MSCTFIME UI"): they belong
            // to the same process as the app (e.g. UWP apps like Windows Settings)
            // but are invisible input plumbing, never app windows to show or open.
            var className = new StringBuilder(256);
            User32.GetClassName(hWnd, className, 256);
            string cls = className.ToString().Trim();
            if (string.Equals(cls, "IME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "MSCTFIME UI", StringComparison.OrdinalIgnoreCase))
                return true;

            // A UWP app shown normally lives in a CoreWindow that is a child of
            // its ApplicationFrameHost frame, so no top-level window of the app's
            // own exe exists: the frame itself is the app's window.
            if (IsFrameHostingExecutable(hWnd, targetPath))
            {
                if (!windows.Any(w => w.Handle == hWnd))
                    windows.Add(new WindowInfo(hWnd, title));
                return true;
            }

            bool matches = IsWindowFromExecutable(hWnd, targetPath)
                || (matchByAppId && windowAppId!(hWnd) is { } appId
                    && appIds!.Contains(appId, StringComparer.OrdinalIgnoreCase));
            if (matches)
            {
                IntPtr handle = ResolveOpenWindow(hWnd, title);
                if (!windows.Any(w => w.Handle == handle))
                    windows.Add(new WindowInfo(handle, title));
            }

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// True when <paramref name="hWnd"/> is an ApplicationFrameHost frame hosting
    /// the app <paramref name="targetPath"/> (not ApplicationFrameHost itself):
    /// its CoreWindow child runs that exe, or did when the frame was last seen
    /// (minimized frames lose the child).
    /// </summary>
    private static bool IsFrameHostingExecutable(IntPtr hWnd, string targetPath)
    {
        if (string.Equals(Path.GetFileName(targetPath), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        string? framePath = GetProcessPath(hWnd);
        if (!string.Equals(Path.GetFileName(framePath), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        string? appPath = GetChildProcessExePath(hWnd, framePath!);
        if (!string.IsNullOrEmpty(appPath))
            _frameAppExeCache[hWnd] = appPath;
        else
            _frameAppExeCache.TryGetValue(hWnd, out appPath);

        return !string.IsNullOrEmpty(appPath) && IsSameExecutable(appPath, targetPath);
    }

    /// <summary>
    /// For UWP/AppX apps the visible top-level window belongs to
    /// ApplicationFrameHost, while the CoreWindow matching the app's exe is
    /// cloaked by the shell while the app is minimized. A cloaked window cannot be shown or activated, so
    /// the preview/activation target is resolved to the ApplicationFrameHost
    /// frame that hosts the same titled window.
    /// </summary>
    private static IntPtr ResolveOpenWindow(IntPtr hWnd, string title)
    {
        IntPtr frame = FindFrameWithTitle(title);
        if (frame != IntPtr.Zero)
            return frame;
        return hWnd;
    }

    /// <summary>Finds a visible, non-cloaked ApplicationFrameHost frame with the given title.</summary>
    private static IntPtr FindFrameWithTitle(string title)
    {
        IntPtr result = IntPtr.Zero;
        if (string.IsNullOrEmpty(title))
            return result;

        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd) || DwmThumbnailInterop.IsCloaked(hWnd))
                return true;

            int exStyle = User32.GetWindowLongPtr(hWnd, Win32Constants.GWL_EXSTYLE).ToInt32();
            if ((exStyle & Win32Constants.WS_EX_TOOLWINDOW) != 0)
                return true;

            string? path = GetProcessPath(hWnd);
            if (!string.Equals(System.IO.Path.GetFileName(path),
                    "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            var titleBuilder = new StringBuilder(1024);
            User32.GetWindowText(hWnd, titleBuilder, 1024);
            if (string.Equals(titleBuilder.ToString().Trim(), title, StringComparison.OrdinalIgnoreCase))
            {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsProgman(IntPtr hWnd)
    {
        var classNameBuilder = new StringBuilder(256);
        User32.GetClassName(hWnd, classNameBuilder, 256);
        return string.Equals(classNameBuilder.ToString().Trim(),
            "Progman", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Brings the window to the foreground, restoring it first only when it is
    /// minimized. SW_RESTORE on a visible snapped or maximized window would
    /// "restore down" to its floating size, which the taskbar never does.
    /// </summary>
    public static void ActivateWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        // A restore posted to an elevated window (see ShowWindowOrPost) is
        // handled by the app itself, which then activates its window: let its
        // process take the foreground, which the dock may give as it received
        // the click.
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
            User32.AllowSetForegroundWindow(pid);

        if (User32.IsIconic(hwnd))
            ShowWindowOrPost(hwnd, Win32Constants.SW_RESTORE, Win32Constants.SC_RESTORE);
        User32.SetForegroundWindow(hwnd);
    }

    /// <summary>Minimizes the window (taskbar click on the already active window).</summary>
    public static void MinimizeWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            ShowWindowOrPost(hwnd, Win32Constants.SW_MINIMIZE, Win32Constants.SC_MINIMIZE);
    }

    /// <summary>
    /// ShowWindow, or the equivalent window-menu command when it is refused.
    /// The dock runs without elevation, and User Interface Privilege Isolation
    /// denies ShowWindow on the windows of elevated apps (a Terminal run as
    /// administrator) with ERROR_ACCESS_DENIED, so their minimize and restore
    /// silently did nothing. WM_SYSCOMMAND still reaches them, and is what the
    /// window's own minimize button and the taskbar thumbnail menu send.
    /// </summary>
    private static void ShowWindowOrPost(IntPtr hwnd, int showCommand, int sysCommand)
    {
        Marshal.SetLastPInvokeError(0);
        if (User32.ShowWindow(hwnd, showCommand)
            || Marshal.GetLastPInvokeError() != Win32Constants.ERROR_ACCESS_DENIED)
            return;
        User32.PostMessage(hwnd, (uint)Win32Constants.WM_SYSCOMMAND, new IntPtr(sysCommand), IntPtr.Zero);
    }

    public static bool IsMinimized(IntPtr hwnd) => hwnd != IntPtr.Zero && User32.IsIconic(hwnd);

    /// <summary>
    /// Asks the window to close by posting WM_CLOSE (the same message the
    /// taskbar sends), giving the app a chance to prompt before closing.
    /// </summary>
    public static void CloseWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        User32.PostMessage(hwnd, (uint)Win32Constants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool IsWindowFromExecutable(IntPtr hWnd, string targetPath)
    {
        User32.GetWindowThreadProcessId(hWnd, out uint pid);

        IntPtr process = Kernel32.OpenProcess(
            Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

        if (process == IntPtr.Zero)
            return false;

        try
        {
            var pathBuffer = new StringBuilder(1024);
            uint size = (uint)pathBuffer.Capacity;

            if (Kernel32.QueryFullProcessImageName(process, 0, pathBuffer, ref size))
            {
                string processPath = NormalizePath(pathBuffer.ToString(0, (int)size));
                return IsSameExecutable(processPath, targetPath);
            }
            return false;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    private static bool IsSameExecutable(string processPath, string targetPath)
    {
        if (string.IsNullOrEmpty(processPath) || string.IsNullOrEmpty(targetPath))
            return false;

        // Exact, case-insensitive path match (Windows paths are case-insensitive).
        if (string.Equals(processPath, targetPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Fallback: match only the filename when the full path isn't comparable.
        string processFile = Path.GetFileName(processPath);
        string targetFile = Path.GetFileName(targetPath);
        return string.Equals(processFile, targetFile, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        path = Path.GetFullPath(path).Trim();
        // Strip Windows extended-length path prefix if present.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];
        return path;
    }
}
