namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Windows acrylic for any window: DWM blurs what is behind it and lays a
/// tint over the blur (accent policy ACCENT_ENABLE_ACRYLICBLURBEHIND, the
/// mechanism behind the shell's acrylic surfaces). Works on popup windows,
/// whose transparency-level hint Avalonia does not apply.
/// </summary>
public static class AcrylicBackdrop
{
    private const int WCA_ACCENT_POLICY = 19;
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
    /// transparency). Returns false when the system refuses it.
    /// </summary>
    public static bool Enable(IntPtr hwnd, byte alpha, byte r, byte g, byte b)
    {
        if (hwnd == IntPtr.Zero) return false;
        var policy = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = (uint)(alpha << 24 | b << 16 | g << 8 | r)
        };
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
