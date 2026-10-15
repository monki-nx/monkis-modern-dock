using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using CedroModernDock.Core.Models;

namespace CedroModernDock.ViewModels;

/// <summary>
/// A taskbar badge drawn on the bottom-right corner of a dock icon, where the
/// Windows 11 taskbar draws it: the app's overlay icon, an unread-count pill,
/// or a dot for glyph badges (newMessage, alert...). Sizes follow the icon.
/// </summary>
public sealed class DockBadgeViewModel
{
    private const int MaxShownCount = 99;

    public TaskbarBadge Source { get; }

    /// <summary>Overlay icon of a desktop app (null for badge notifications).</summary>
    public Bitmap? Overlay { get; }

    /// <summary>Unread count ("3", "99+"), or null.</summary>
    public string? Text { get; }

    public bool ShowOverlay => Overlay != null;
    public bool ShowCount => Overlay == null && Text != null;
    public bool ShowDot => Overlay == null && Text == null;

    public string? ToolTip { get; }

    public double Size { get; }
    public double DotSize => Math.Round(Size * 0.6);
    public double FontSize => Math.Round(Size * 0.62);
    public CornerRadius PillRadius => new(Size / 2);

    private DockBadgeViewModel(TaskbarBadge source, Bitmap? overlay, int iconSize)
    {
        Source = source;
        Overlay = overlay;
        Text = source.Count is int count
            ? (count > MaxShownCount ? $"{MaxShownCount}+" : count.ToString())
            : null;
        ToolTip = source.Description ?? Text;
        Size = Math.Max(12, Math.Round(iconSize * (overlay != null ? 0.45 : 0.38)));
    }

    public static DockBadgeViewModel From(TaskbarBadge badge, int iconSize)
    {
        Bitmap? overlay = null;
        if (badge.OverlayPng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new MemoryStream(png);
                overlay = new Bitmap(stream);
            }
            catch { /* unreadable overlay: fall back to a dot */ }
        }
        return new DockBadgeViewModel(badge, overlay, iconSize);
    }
}
