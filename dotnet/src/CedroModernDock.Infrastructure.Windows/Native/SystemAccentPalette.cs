namespace CedroModernDock.Infrastructure.Windows.Native;

using Microsoft.Win32;

/// <summary>
/// The accent shades the Windows shell paints with (taskbar calendar, Start),
/// read from Explorer's AccentPalette: 8 RGBA entries from the lightest
/// (Light3) through the accent itself to the darkest (Dark3).
/// </summary>
public static class SystemAccentPalette
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    public const int Light3 = 0;
    public const int Light2 = 1;
    public const int Light1 = 2;
    public const int Accent = 3;
    public const int Dark1 = 4;
    public const int Dark2 = 5;
    public const int Dark3 = 6;

    /// <summary>Returns the shade as (r, g, b), or null when the palette is unavailable.</summary>
    public static (byte R, byte G, byte B)? Shade(int index)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= (index + 1) * 4)
                return (palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
        }
        catch { /* unreadable: caller falls back */ }
        return null;
    }
}
