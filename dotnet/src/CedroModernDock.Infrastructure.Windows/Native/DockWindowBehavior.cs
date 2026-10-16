namespace CedroModernDock.Infrastructure.Windows.Native;

using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Applies dock-specific Win32 window behavior that Avalonia alone cannot provide:
/// <list type="bullet">
/// <item><b>WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW</b> — the dock never steals focus
/// and never appears in the taskbar or Alt-Tab cycle.</item>
/// <item><b>Desktop attachment</b> — the dock becomes an owned window of the
/// desktop (the WorkerW hosting the desktop icons, or Progman) kept at the
/// bottom of the z-order, so it sits behind every normal window like the
/// desktop icons. It stays a top-level window: DWM gives those the acrylic
/// backdrop it refuses to a window parented to the desktop.</item>
/// <item><b>Subclass</b> — keeps the dock bottommost (WM_WINDOWPOSCHANGING)
/// and answers Win+D "Show Desktop": minimize commands are blocked
/// (SC_MINIMIZE) and a forced minimize is undone (SIZE_MINIMIZED).</item>
/// </list>
/// </summary>
public sealed class DockWindowBehavior : IDisposable
{
    private IntPtr _hwnd;
    private readonly Action<string>? _onStatus;
    private SubclassProc? _subclassProc; // kept alive to prevent GC of the native callback
    private bool _subclassed;

    /// <summary>Window message the shell uses for this dock's appbar notifications.</summary>
    public uint AppBarCallbackMessage { get; } = User32.RegisterWindowMessage("CedroModernDock.AppBar");

    /// <summary>Raised with the notification code (wParam) of each appbar notification.</summary>
    public Action<int>? AppBarNotification { get; set; }

    public DockWindowBehavior(IntPtr hwnd, Action<string>? onStatus = null)
    {
        _hwnd = hwnd;
        _onStatus = onStatus;
    }

    public void Apply()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _onStatus?.Invoke("ERROR: HWND is zero — window not opened yet?");
            return;
        }

        ApplyExtendedStyles();
        AttachToDesktop();

        // Store the delegate in a field so it is not garbage-collected while
        // the native subclass is active (would cause a crash on next message).
        _subclassProc = new SubclassProc(HandleMessage);
        _subclassed = Comctl32.SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, IntPtr.Zero);

        _onStatus?.Invoke($"HWND=0x{_hwnd:X} | Subclass={(_subclassed ? "OK" : "FAIL")}");
    }

    private void ApplyExtendedStyles()
    {
        IntPtr exStylePtr = User32.GetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE);
        int exStyle = exStylePtr.ToInt32();

        exStyle |= Win32Constants.WS_EX_NOACTIVATE | Win32Constants.WS_EX_TOOLWINDOW;
        exStyle &= ~Win32Constants.WS_EX_APPWINDOW; // remove taskbar entry

        User32.SetWindowLongPtr(_hwnd, Win32Constants.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>
    /// Makes the desktop icons window the dock's owner and sends the dock to
    /// the bottom of the z-order, where the subclass keeps it. Owned rather
    /// than parented, the dock stays a top-level window in screen coordinates
    /// and keeps the acrylic backdrop; owned by the desktop and bottommost, it
    /// sits behind every normal window — exactly like the desktop icons.
    /// </summary>
    private void AttachToDesktop()
    {
        IntPtr desktop = FindDesktopWindow();
        if (desktop == IntPtr.Zero)
        {
            _onStatus?.Invoke("AttachToDesktop: desktop window not found");
            return;
        }

        User32.SetWindowLongPtr(_hwnd, Win32Constants.GWLP_HWNDPARENT, desktop);
        User32.SetWindowPos(_hwnd, Win32Constants.HWND_BOTTOM, 0, 0, 0, 0,
            Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE);
        _onStatus?.Invoke($"Owned by desktop 0x{desktop:X}");
    }

    /// <summary>
    /// Screen position of the parent's client origin: (0,0) for the dock,
    /// which is a top-level window (owned by the desktop, not parented to it).
    /// A window parented to the desktop would move relative to it, and the
    /// desktop starts at the virtual screen's top-left — not (0,0) when a
    /// monitor sits above or left of the primary one.
    /// </summary>
    public (int X, int Y) GetParentOrigin()
    {
        // GetParent would return the owner: the dock keeps WS_POPUP.
        IntPtr parent = _hwnd == IntPtr.Zero ? IntPtr.Zero : User32.GetAncestor(_hwnd, User32.GA_PARENT);
        if (parent == IntPtr.Zero || parent == User32.GetDesktopWindow()) return (0, 0);
        var origin = new POINT();
        return User32.ClientToScreen(parent, ref origin) ? (origin.X, origin.Y) : (0, 0);
    }

    /// <summary>
    /// Locates the desktop window that hosts the desktop icons: the WorkerW
    /// containing a SHELLDLL_DefView child (created on demand by Progman).
    /// Falls back to Progman itself when no such WorkerW exists.
    /// </summary>
    private static IntPtr FindDesktopWindow()
    {
        IntPtr progman = User32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        // Ask Progman to create the icons WorkerW if it does not exist yet.
        User32.SendMessageTimeout(progman, Win32Constants.WM_PROGMAN_CREATE_DESKTOP,
            IntPtr.Zero, IntPtr.Zero, Win32Constants.SMTO_ABORTIFHUNG, 1000, out _);

        IntPtr workerW = FindWorkerWWithIcons();
        return workerW != IntPtr.Zero ? workerW : progman;
    }

    private static IntPtr FindWorkerWWithIcons()
    {
        IntPtr found = IntPtr.Zero;
        User32.EnumWindows((hWnd, _) =>
        {
            var className = new StringBuilder(256);
            User32.GetClassName(hWnd, className, className.Capacity);
            if (className.ToString() == "WorkerW" &&
                User32.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private IntPtr HandleMessage(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == AppBarCallbackMessage && AppBarCallbackMessage != 0)
        {
            AppBarNotification?.Invoke(wParam.ToInt32());
            return IntPtr.Zero;
        }

        // 0. Bottommost: whatever tries to raise the dock lands it at the
        //    bottom of the z-order instead, behind every normal window (its
        //    desktop owner keeps it above the wallpaper and the icons).
        if (uMsg == Win32Constants.WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & Win32Constants.SWP_NOZORDER) == 0 && pos.hwndInsertAfter != Win32Constants.HWND_BOTTOM)
            {
                pos.hwndInsertAfter = Win32Constants.HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }

        // 1. Block explicit minimize (SC_MINIMIZE) — covers normal "Minimize"
        //    clicks and Win+D "Show Desktop" (which sends SC_MINIMIZE).
        if (uMsg == Win32Constants.WM_SYSCOMMAND)
        {
            int command = wParam.ToInt32() & 0xFFF0;
            if (command == Win32Constants.SC_MINIMIZE)
            {
                _onStatus?.Invoke("Blocked SC_MINIMIZE");
                return IntPtr.Zero; // swallow the message; do not minimize
            }
        }

        // 2. Win+D may also minimize windows through other paths. If we receive
        //    WM_SIZE with SIZE_MINIMIZED, undo it so the dock stays on the desktop.
        if (uMsg == Win32Constants.WM_SIZE && wParam.ToInt32() == Win32Constants.SIZE_MINIMIZED)
        {
            User32.ShowWindow(_hwnd, Win32Constants.SW_RESTORE);
            _onStatus?.Invoke("Restored from SIZE_MINIMIZED (Win+D defense)");
        }

        return Comctl32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_subclassed && _subclassProc != null && _hwnd != IntPtr.Zero)
        {
            Comctl32.RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
            _subclassed = false;
        }

        _subclassProc = null;
        _hwnd = IntPtr.Zero;
    }
}
