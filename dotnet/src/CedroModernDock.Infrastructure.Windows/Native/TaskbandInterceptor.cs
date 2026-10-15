using System.Runtime.InteropServices;
using static CedroModernDock.Infrastructure.Windows.Native.TaskbandInterop;

namespace CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Sees the overlay icons apps set on their taskbar buttons without taking
/// them away from the taskbar.
/// <para>
/// ITaskbarList3 (SetOverlayIcon, SetProgressValue, thumbnail toolbars...)
/// delivers every call as a WM_USER+N message sent to the window stored in the
/// "TaskbandHWND" property of Explorer's Shell_TrayWnd. The interceptor points
/// that property at a hidden window of its own, reads the overlay messages and
/// forwards every message, unchanged and synchronously, to Explorer's taskband,
/// so the taskbar keeps showing overlays, progress and thumbnail buttons.
/// Stopping (or a "TaskbarCreated" from a restarted Explorer) points the
/// property back at Explorer's taskband.
/// </para>
/// <para>
/// The window runs its own message loop on a dedicated thread: apps calling
/// ITaskbarList3 block until their message is handled, so they must never
/// wait on the dock's UI thread. Event handlers run on that thread while the
/// sender is still blocked, which keeps the HICON and the shared description
/// valid; they must copy what they need and return quickly.
/// </para>
/// </summary>
internal sealed class TaskbandInterceptor : IDisposable
{
    private const string ClassName = "CedroDockTaskbandInterceptor";
    private const string TaskbandProperty = "TaskbandHWND";
    private const string TaskbandClass = "MSTaskSwWClass";

    // Undocumented ITaskbarList messages: wParam is the app window, lParam the
    // HICON (0 clears the overlay) or the SHAllocShared description string.
    private const uint MsgSetOverlayIcon = WM_USER + 79;
    private const uint MsgSetOverlayDescription = WM_USER + 85;
    private const uint MsgStop = WM_APP + 1;

    /// <summary>Upper bound for Explorer to handle a forwarded message (the sender waits that long).</summary>
    private const uint ForwardTimeoutMs = 5000;

    private static readonly IntPtr _hInstance = Kernel32.GetModuleHandle(null);
    private static readonly uint _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static readonly uint _taskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    private static readonly object _classLock = new();
    private static bool _classRegistered;

    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private IntPtr _hwnd;
    private IntPtr _tray;
    private IntPtr _taskband;

    /// <summary>(app window, HICON or 0). Raised on the interceptor thread while the HICON is valid.</summary>
    public event Action<IntPtr, IntPtr>? OverlayIconSet;

    /// <summary>(app window, description or null). Raised on the interceptor thread.</summary>
    public event Action<IntPtr, string?>? OverlayDescriptionSet;

    /// <summary>Raised after the interceptor took over the taskband again (Explorer restarted).</summary>
    public event Action? Rehooked;

    /// <summary>True while overlay messages reach this interceptor.</summary>
    public bool IsIntercepting { get; private set; }

    /// <summary>
    /// Creates the hidden window and takes over the taskband property.
    /// Returns false when there is no Explorer taskbar to intercept.
    /// </summary>
    public bool Start()
    {
        if (_thread != null) return IsIntercepting;
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskbandInterceptor" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
        return IsIntercepting;
    }

    /// <summary>
    /// Asks every taskbar window to set up its taskbar button again (as Explorer
    /// does when it starts), so apps send the overlay they already show.
    /// </summary>
    public static void RequestButtonRefresh(IEnumerable<IntPtr> windows)
    {
        foreach (var hwnd in windows)
            SendNotifyMessage(hwnd, _taskbarButtonCreatedMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        var thread = _thread;
        if (thread == null) return;
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, MsgStop, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(5));
        _thread = null;
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            RegisterWindowClass();
            _hwnd = User32.CreateWindowEx(
                (uint)Win32Constants.WS_EX_TOOLWINDOW, ClassName, "",
                (uint)Win32Constants.WS_POPUP, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return;
            Hook();
        }
        finally
        {
            _ready.Set();
        }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void RegisterWindowClass()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;
            User32.RegisterClass(new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticWndProc),
                hInstance = _hInstance,
                lpszClassName = ClassName
            });
            _classRegistered = true;
        }
    }

    // The class procedure is shared; it routes to the instance that owns the
    // window. Kept in a static field: the window class holds a pointer to it.
    private static readonly Dictionary<IntPtr, TaskbandInterceptor> _instances = new();
    private static readonly WndProc StaticWndProc = (hwnd, msg, wParam, lParam) =>
    {
        TaskbandInterceptor? owner;
        lock (_instances) _instances.TryGetValue(hwnd, out owner);
        return owner != null
            ? owner.WindowProc(hwnd, msg, wParam, lParam)
            : User32.DefWindowProc(hwnd, msg, wParam, lParam);
    };

    /// <summary>Points the taskband property at this interceptor's window.</summary>
    private void Hook()
    {
        lock (_instances) _instances[_hwnd] = this;
        IsIntercepting = false;

        _tray = FindWindow("Shell_TrayWnd", null);
        if (_tray == IntPtr.Zero) return;
        _taskband = FindExplorerTaskband(_tray);
        if (_taskband == IntPtr.Zero) return;
        IsIntercepting = SetProp(_tray, TaskbandProperty, _hwnd);
    }

    /// <summary>Gives the taskband property back to Explorer, unless someone else took it since.</summary>
    private void Unhook()
    {
        if (IsIntercepting && IsWindow(_tray) && IsWindow(_taskband)
            && GetProp(_tray, TaskbandProperty) == _hwnd)
            SetProp(_tray, TaskbandProperty, _taskband);
        IsIntercepting = false;
    }

    /// <summary>
    /// Explorer's taskband window. The property normally points at it; when it
    /// does not (a previous dock run ended without restoring it), the window is
    /// looked up among the taskbar's descendants.
    /// </summary>
    private IntPtr FindExplorerTaskband(IntPtr tray)
    {
        var current = GetProp(tray, TaskbandProperty);
        if (current != IntPtr.Zero && current != _hwnd && IsWindow(current)
            && ClassNameOf(current) == TaskbandClass)
            return current;

        IntPtr found = IntPtr.Zero;
        EnumChildWindows(tray, (child, _) =>
        {
            if (ClassNameOf(child) != TaskbandClass) return true;
            found = child;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == MsgStop)
        {
            Unhook();
            lock (_instances) _instances.Remove(hwnd);
            User32.DestroyWindow(hwnd);
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: its new taskbar has a fresh property.
            Hook();
            if (IsIntercepting) Rehooked?.Invoke();
            return IntPtr.Zero;
        }

        if (msg >= WM_USER && msg < WM_APP)
        {
            try
            {
                if (msg == MsgSetOverlayIcon)
                    OverlayIconSet?.Invoke(wParam, lParam);
                else if (msg == MsgSetOverlayDescription)
                    OverlayDescriptionSet?.Invoke(wParam, ReadSharedString(lParam, wParam));
            }
            catch { /* a badge must never break the taskbar */ }
            return Forward(msg, wParam, lParam);
        }

        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr Forward(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!IsWindow(_taskband)) return IntPtr.Zero;
        SendMessageTimeout(_taskband, msg, wParam, lParam,
            SMTO_NORMAL | SMTO_ABORTIFHUNG, ForwardTimeoutMs, out var result);
        return result;
    }

    /// <summary>Reads a string the sender put in SHAllocShared memory.</summary>
    private static string? ReadSharedString(IntPtr shared, IntPtr senderWindow)
    {
        if (shared == IntPtr.Zero) return null;
        GetWindowThreadProcessId(senderWindow, out uint processId);
        IntPtr data = SHLockShared(shared, processId);
        if (data == IntPtr.Zero) return null;
        try
        {
            string? text = Marshal.PtrToStringUni(data);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            SHUnlockShared(data);
        }
    }
}
