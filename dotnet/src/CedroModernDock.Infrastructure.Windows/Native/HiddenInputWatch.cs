using System.Runtime.InteropServices;

namespace CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Notices clicks the dock cannot see. The dock runs without elevation, and
/// User Interface Privilege Isolation hides the mouse and keyboard from it
/// while the foreground window belongs to an elevated app (a Terminal run as
/// administrator): GetAsyncKeyState, raw input and low-level hooks all stay
/// silent. Only the time of the last input (any device) and the cursor
/// position remain visible, so input that arrives with the cursor at rest
/// is taken for a button press or a key — moving the mouse always moves the
/// cursor. A dock popup polls this to close when the user clicks the
/// elevated app it was opened over.
/// </summary>
public sealed class HiddenInputWatch
{
    private uint _lastInputTick;
    private POINT _cursor;

    /// <summary>Starts watching from now: input that came before is ignored.</summary>
    public void Reset()
    {
        _lastInputTick = LastInputTick();
        User32.GetCursorPos(out _cursor);
    }

    /// <summary>
    /// True when input hidden from the dock arrived since the last poll with
    /// the cursor at rest, and the foreground window is above the dock's
    /// integrity level (otherwise the dock sees the buttons itself).
    /// </summary>
    public bool HiddenPressSinceLastPoll()
    {
        uint tick = LastInputTick();
        User32.GetCursorPos(out POINT cursor);
        bool newInput = tick != _lastInputTick;
        bool atRest = cursor.X == _cursor.X && cursor.Y == _cursor.Y;
        _lastInputTick = tick;
        _cursor = cursor;
        return newInput && atRest && IsAboveOwnIntegrity(User32.GetForegroundWindow());
    }

    private static uint LastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return User32.GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    private static readonly int OwnIntegrity =
        IntegrityLevel((uint)Environment.ProcessId) ?? SECURITY_MANDATORY_MEDIUM_RID;

    /// <summary>
    /// True when the window's process runs at a higher integrity level than
    /// the dock. A process whose token cannot be read is taken as higher:
    /// that is how an elevated process looks from a non-elevated one.
    /// </summary>
    public static bool IsAboveOwnIntegrity(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        int? level = IntegrityLevel(pid);
        return level == null || level > OwnIntegrity;
    }

    private static int? IntegrityLevel(uint pid)
    {
        IntPtr process = Kernel32.OpenProcess(Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!Advapi32.OpenProcessToken(process, TOKEN_QUERY, out token)) return null;
            Advapi32.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int size);
            if (size <= 0) return null;
            buffer = Marshal.AllocHGlobal(size);
            if (!Advapi32.GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return null;
            // TOKEN_MANDATORY_LABEL: the label's SID comes first; its last
            // sub-authority is the integrity level (0x2000 medium, 0x3000 high).
            IntPtr sid = Marshal.ReadIntPtr(buffer);
            int count = Marshal.ReadByte(Advapi32.GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(Advapi32.GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) Kernel32.CloseHandle(token);
            Kernel32.CloseHandle(process);
        }
    }

    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int SECURITY_MANDATORY_MEDIUM_RID = 0x2000;
}
