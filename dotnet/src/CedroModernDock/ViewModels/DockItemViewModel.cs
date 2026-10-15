namespace CedroModernDock.ViewModels;

using System.Windows.Input;
using Avalonia.Media.Imaging;
using CedroModernDock.Core.Models;

/// <summary>
/// ViewModel for a single dock item. Holds the icon bitmap, localized label,
/// running-app indicator state, and click-to-launch command.
/// </summary>
public class DockItemViewModel : ViewModelBase
{
    private Bitmap? _icon;
    private bool _isRunning;

    /// <summary>The underlying dock item model.</summary>
    public DockItem Item { get; }

    /// <summary>Localized display label (shown in tooltip).</summary>
    public string Label { get; }

    /// <summary>The icon bitmap to render (loaded from cache or assets).</summary>
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    /// <summary>True when the program has at least one open window (drives indicator dot).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
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

    /// <summary>True if this item should show a running-app indicator (program items only).</summary>
    public bool ShowIndicator { get; }

    /// <summary>The icon render size in pixels (mirrors the dock's IconsSize setting).</summary>
    public int IconSize { get; set; } = 48;

    /// <summary>The executable path for running-indicator polling (program items only).</summary>
    public string? ExecutablePath { get; }

    /// <summary>Command invoked when the dock item is clicked.</summary>
    public ICommand ClickCommand { get; }

    public DockItemViewModel(
        DockItem item,
        string label,
        ICommand clickCommand,
        bool showIndicator = false,
        string? executablePath = null)
    {
        Item = item;
        Label = label;
        ClickCommand = clickCommand;
        ShowIndicator = showIndicator;
        ExecutablePath = executablePath;
    }
}
