namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Windows acrylic for any window: DWM blurs what is behind it and lays a
/// tint over the blur (accent policy ACCENT_ENABLE_ACRYLICBLURBEHIND, the
/// mechanism behind the shell's acrylic surfaces). Works on popup windows,
/// whose transparency-level hint Avalonia does not apply, and on any other
/// top-level window; DWM refuses it to a window that has a parent.
/// </summary>
public static class AcrylicBackdrop
{
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor; // AABBGGRR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>
    /// Enables acrylic with the given tint (the blur shows through the tint's
    /// transparency). Returns false when the system refuses it, or when the
    /// user has switched Windows' transparency effects off: the shell then
    /// draws its own surfaces flat, and the accent would show as a plain
    /// rectangle instead of a blur.
    /// </summary>
    public static bool Enable(IntPtr hwnd, byte alpha, byte r, byte g, byte b)
    {
        if (hwnd == IntPtr.Zero || !SystemTransparencyEnabled()) return false;
        // A fully transparent tint switches the blur off altogether, which
        // would leave the dock bar invisible at intensity 0; the faintest
        // tint keeps the blur.
        if (alpha == 0) alpha = 1;
        return Apply(hwnd, new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = (uint)(alpha << 24 | b << 16 | g << 8 | r)
        });
    }

    /// <summary>Removes the acrylic (or any accent) from the window.</summary>
    public static bool Disable(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && Apply(hwnd, new AccentPolicy { AccentState = ACCENT_DISABLED });

    /// <summary>Windows' "Transparency effects" setting (Personalization > Colors).</summary>
    private static bool SystemTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool Apply(IntPtr hwnd, AccentPolicy policy)
    {
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
