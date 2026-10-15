using Avalonia.Media.Imaging;
using CedroModernDock.Core.Models;

namespace CedroModernDock.ViewModels;

/// <summary>
/// A program that is running with open window(s) but is NOT pinned to the dock.
/// Shown in the running-apps section (right of the separator), mirroring the
/// Windows taskbar. Hovering shows the window-preview popup; there is no click action.
/// </summary>
public class RunningAppViewModel : ViewModelBase
{
    private Bitmap? _icon;
    private bool _isRunning;

    /// <summary>The owning executable path (used to group windows and load the icon).</summary>
    public string ExecutablePath { get; }

    /// <summary>Display label for the tooltip (fallback to executable name).</summary>
    public string Label { get; }

    /// <summary>The icon bitmap to render (tinted when the icon tint is enabled).</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>The untinted icon bitmap, kept so the tint can be re-applied
    /// when the tint color or toggle changes (these VMs persist between refreshes).</summary>
    public Bitmap? OriginalIcon { get; set; }

    /// <summary>True while the app has at least one open window.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    /// <summary>The icon render size in pixels (mirrors the dock's IconsSize setting).</summary>
    private int _iconSize = 48;
    public int IconSize
    {
        get => _iconSize;
        set
        {
            if (SetProperty(ref _iconSize, value) && _badgeSource != null)
                Badge = DockBadgeViewModel.From(_badgeSource, value);
        }
    }

    private TaskbarBadge? _badgeSource;
    private DockBadgeViewModel? _badge;

    /// <summary>The taskbar badge the app shows (overlay icon, count or glyph), or null.</summary>
    public DockBadgeViewModel? Badge
    {
        get => _badge;
        private set
        {
            if (SetProperty(ref _badge, value))
                OnPropertyChanged(nameof(ToolTipText));
        }
    }

    /// <summary>Label plus the badge's description ("Outlook — 5 unread messages").</summary>
    public string ToolTipText => Badge?.ToolTip is string detail ? $"{Label} — {detail}" : Label;

    /// <summary>Shows <paramref name="badge"/>; an unchanged badge touches nothing.</summary>
    public void SetBadge(TaskbarBadge? badge)
    {
        if (Equals(badge, _badgeSource)) return;
        _badgeSource = badge;
        Badge = badge == null ? null : DockBadgeViewModel.From(badge, IconSize);
    }

    public RunningAppViewModel(string executablePath, string label)
    {
        ExecutablePath = executablePath;
        Label = label;
        IsRunning = true;
    }
}
