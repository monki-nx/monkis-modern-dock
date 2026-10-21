namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Registers the dock as a Windows application desktop toolbar (appbar) on
/// the bottom edge of its monitor, the mechanism the taskbar uses: the shell
/// shrinks that monitor's work area by the reserved strip, so maximized
/// windows stop above the dock instead of covering it.
/// </summary>
/// <remarks>
/// The shell forgets every appbar when Explorer restarts, and after a
/// display change it may stop applying a strip it still lists; a dock that
/// only remembers "I registered once" then never reserves again. Every
/// reservation is therefore verified against the monitor's work area and
/// re-registered from scratch when the shell did not apply it, and
/// <see cref="ShellRestarted"/> drops the registration outright.
/// </remarks>
public sealed class DockAppBar : IDisposable
{
    private const uint ABM_NEW = 0x00000000;
    private const uint ABM_REMOVE = 0x00000001;
    private const uint ABM_QUERYPOS = 0x00000002;
    private const uint ABM_SETPOS = 0x00000003;
    private const uint ABE_BOTTOM = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>ABN_POSCHANGED: another appbar or the taskbar moved; re-check the strip.</summary>
    public const int ABN_POSCHANGED = 0x00000001;

    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private readonly Action<string>? _onStatus;
    private bool _registered;
    private RECT _requested;

    /// <summary>
    /// The strip currently reserved (screen coordinates), or null. Screen
    /// geometry queries add it back so the dock anchors inside its own strip.
    /// </summary>
    public static RECT? Reserved { get; private set; }

    public DockAppBar(IntPtr hwnd, uint callbackMessage, Action<string>? onStatus = null)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
        _onStatus = onStatus;
    }

    /// <summary>
    /// Reserves the strip from the dock's top edge down to the bottom of its
    /// monitor. A dock far from the bottom edge (more than a quarter of the
    /// monitor) reserves nothing. Calling it again with the same geometry only
    /// verifies that the shell still applies the strip; <paramref name="force"/>
    /// resends it regardless.
    /// </summary>
    public void ReserveBottom(RECT dock, bool force = false)
    {
        IntPtr monitor = MonitorFromRect(ref dock, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero || !TryGetMonitorInfo(monitor, out MONITORINFO info))
            return;

        RECT screen = info.rcMonitor;
        int height = screen.Bottom - screen.Top;
        if (screen.Bottom - dock.Bottom > height / 4)
        {
            Remove();
            return;
        }

        var wanted = new RECT { Left = screen.Left, Top = dock.Top, Right = screen.Right, Bottom = screen.Bottom };
        bool changed = !_registered || !Same(wanted, _requested);
        if (!changed && !force && Reserved is { } current && IsApplied(monitor, current))
            return;
        _requested = wanted;

        if (!Register())
            return;

        RECT granted = SetPos(wanted, dock.Top);
        if (IsApplied(monitor, granted))
        {
            Reserved = granted;
            return;
        }

        // The shell answered but the work area did not shrink: its record of
        // this appbar is stale (seen after display changes). Register anew.
        _onStatus?.Invoke("AppBar strip not applied; re-registering");
        Unregister();
        if (!Register())
            return;
        granted = SetPos(wanted, dock.Top);
        Reserved = IsApplied(monitor, granted) ? granted : null;
        if (Reserved == null)
            _onStatus?.Invoke("AppBar strip still not applied after re-registering");
    }

    /// <summary>
    /// The shell restarted (TaskbarCreated): it forgot every appbar, so the
    /// next <see cref="ReserveBottom"/> must register again.
    /// </summary>
    public void ShellRestarted()
    {
        _registered = false;
        _requested = default;
        Reserved = null;
    }

    /// <summary>Releases the reserved strip (the work area grows back).</summary>
    public void Remove()
    {
        if (!_registered) return;
        Unregister();
        _requested = default;
        Reserved = null;
    }

    public void Dispose() => Remove();

    private bool Register()
    {
        if (_registered) return true;
        var registration = NewData();
        registration.uCallbackMessage = _callbackMessage;
        _registered = SHAppBarMessage(ABM_NEW, ref registration) != 0;
        if (!_registered)
            _onStatus?.Invoke("ABM_NEW failed");
        return _registered;
    }

    private void Unregister()
    {
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    /// <summary>
    /// Proposes the strip and accepts where the shell puts it (e.g. above a
    /// taskbar on the same edge), keeping the dock's thickness.
    /// </summary>
    private RECT SetPos(RECT wanted, int dockTop)
    {
        var data = NewData();
        data.uEdge = ABE_BOTTOM;
        data.rc = wanted;
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        int thickness = Math.Max(0, data.rc.Bottom - dockTop);
        data.rc.Top = data.rc.Bottom - thickness;
        SHAppBarMessage(ABM_SETPOS, ref data);
        return data.rc;
    }

    /// <summary>True when the monitor's work area stops at or above the strip.</summary>
    private static bool IsApplied(IntPtr monitor, RECT strip) =>
        TryGetMonitorInfo(monitor, out MONITORINFO info) && info.rcWork.Bottom <= strip.Top;

    private static bool TryGetMonitorInfo(IntPtr monitor, out MONITORINFO info)
    {
        info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info);
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    private static bool Same(RECT a, RECT b) =>
        a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
