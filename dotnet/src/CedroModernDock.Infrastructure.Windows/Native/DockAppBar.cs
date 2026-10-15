namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Registers the dock as a Windows application desktop toolbar (appbar) on
/// the bottom edge of its monitor, the mechanism the taskbar uses: the shell
/// shrinks that monitor's work area by the reserved strip, so maximized
/// windows stop above the dock instead of covering it.
/// </summary>
public sealed class DockAppBar : IDisposable
{
    private const uint ABM_NEW = 0x00000000;
    private const uint ABM_REMOVE = 0x00000001;
    private const uint ABM_QUERYPOS = 0x00000002;
    private const uint ABM_SETPOS = 0x00000003;
    private const uint ABE_BOTTOM = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>ABN_POSCHANGED: another appbar or the taskbar moved; re-reserve.</summary>
    public const int ABN_POSCHANGED = 0x00000001;

    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private bool _registered;
    private RECT _requested;

    /// <summary>
    /// The strip currently reserved (screen coordinates), or null. Screen
    /// geometry queries add it back so the dock anchors inside its own strip.
    /// </summary>
    public static RECT? Reserved { get; private set; }

    public DockAppBar(IntPtr hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
    }

    /// <summary>
    /// Reserves the strip from the dock's top edge down to the bottom of its
    /// monitor. A dock far from the bottom edge (more than a quarter of the
    /// monitor) reserves nothing. Calling it again with the same geometry is
    /// a no-op, which keeps ABN_POSCHANGED notifications from looping.
    /// </summary>
    public void ReserveBottom(RECT dock, bool force = false)
    {
        IntPtr monitor = MonitorFromRect(ref dock, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return;

        RECT screen = info.rcMonitor;
        int height = screen.Bottom - screen.Top;
        if (screen.Bottom - dock.Bottom > height / 4)
        {
            Remove();
            return;
        }

        var wanted = new RECT { Left = screen.Left, Top = dock.Top, Right = screen.Right, Bottom = screen.Bottom };
        if (!force && _registered && Same(wanted, _requested))
            return;
        _requested = wanted;

        if (!_registered)
        {
            var registration = NewData();
            registration.uCallbackMessage = _callbackMessage;
            _registered = SHAppBarMessage(ABM_NEW, ref registration) != 0;
            if (!_registered) return;
        }

        // The shell may move the proposed strip (e.g. above the taskbar on the
        // same edge); keep the dock's thickness from wherever it lands.
        var data = NewData();
        data.uEdge = ABE_BOTTOM;
        data.rc = wanted;
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        int thickness = Math.Max(0, data.rc.Bottom - dock.Top);
        data.rc.Top = data.rc.Bottom - thickness;
        SHAppBarMessage(ABM_SETPOS, ref data);
        Reserved = data.rc;
    }

    /// <summary>Releases the reserved strip (the work area grows back).</summary>
    public void Remove()
    {
        if (!_registered) return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
        _requested = default;
        Reserved = null;
    }

    public void Dispose() => Remove();

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
