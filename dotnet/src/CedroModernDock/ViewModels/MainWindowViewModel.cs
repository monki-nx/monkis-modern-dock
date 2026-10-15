using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;

namespace CedroModernDock.ViewModels;

/// <summary>
/// Main dock ViewModel. Holds the dock items collection, appearance settings,
/// and the running-app indicator watcher. Direct port of DockController's
/// state and update logic, expressed as MVVM bindings.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices? _appServices;
    private CancellationTokenSource? _indicatorCts;

    // Wakes the indicator watcher early when an app changes its taskbar badge.
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    private int _iconsSize = 48;
    private int _spacing = 5;
    private int _borderRounding = 12;
    private IBrush _dockBackground = new SolidColorBrush(Color.FromArgb(158, 0, 0, 0));
    private string _statusText = "";

    public ObservableCollection<DockItemViewModel> Items { get; } = new();

    /// <summary>Running-but-unpinned programs shown right of the separator (taskbar-like).</summary>
    public ObservableCollection<RunningAppViewModel> RunningApps { get; } = new();

    private bool _hasRunningApps;
    public bool HasRunningApps
    {
        get => _hasRunningApps;
        private set => SetProperty(ref _hasRunningApps, value);
    }

    private bool _isVerticalDock;
    public bool IsVerticalDock
    {
        get => _isVerticalDock;
        set
        {
            if (SetProperty(ref _isVerticalDock, value))
            {
                OnPropertyChanged(nameof(DockOrientation));
                OnPropertyChanged(nameof(ShowHorizontalSeparator));
                OnPropertyChanged(nameof(ShowVerticalSeparator));
            }
        }
    }

    /// <summary>Layout direction of the dock items (horizontal by default, vertical when enabled).</summary>
    public Avalonia.Layout.Orientation DockOrientation
        => IsVerticalDock ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;

    /// <summary>
    /// Separator after the pinned items. Always shown: the clock sits right
    /// of it, after the unpinned running apps when there are any.
    /// </summary>
    public bool ShowHorizontalSeparator => !IsVerticalDock;

    /// <summary>Vertical-dock counterpart of <see cref="ShowHorizontalSeparator"/>.</summary>
    public bool ShowVerticalSeparator => IsVerticalDock;

    public int IconsSize
    {
        get => _iconsSize;
        set
        {
            if (SetProperty(ref _iconsSize, value))
                OnPropertyChanged(nameof(ClockFontSize));
        }
    }

    // --- Clock (Windows 11 taskbar style) ---

    private readonly DispatcherTimer _clockTimer = new();
    private string _clockTime = "";
    private string _clockDate = "";
    private string _clockToolTip = "";

    /// <summary>24-hour time, e.g. "14:05".</summary>
    public string ClockTime { get => _clockTime; private set => SetProperty(ref _clockTime, value); }

    /// <summary>Date as DD/MM/YYYY, e.g. "15/10/2026".</summary>
    public string ClockDate { get => _clockDate; private set => SetProperty(ref _clockDate, value); }

    /// <summary>Long date in the system culture, shown on hover like the taskbar clock.</summary>
    public string ClockToolTip { get => _clockToolTip; private set => SetProperty(ref _clockToolTip, value); }

    /// <summary>Month calendar opened by clicking the clock.</summary>
    public CalendarViewModel Calendar { get; } = new();

    /// <summary>Scales with the icons (12 px at the default 48 px icons, as on the taskbar).</summary>
    public double ClockFontSize => Math.Clamp(IconsSize / 4.0, 11, 18);
    public int Spacing { get => _spacing; set => SetProperty(ref _spacing, value); }
    public int BorderRounding
    {
        get => _borderRounding;
        set
        {
            if (SetProperty(ref _borderRounding, value))
                OnPropertyChanged(nameof(DockCornerRadius));
        }
    }
    public CornerRadius DockCornerRadius => new CornerRadius(BorderRounding);
    public IBrush DockBackground { get => _dockBackground; set => SetProperty(ref _dockBackground, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ICommand LaunchCommand { get; }

    /// <summary>Set by MainWindow — opens the settings window with this window as owner.</summary>
    public Action? OpenSettingsAction { get; set; }

    /// <summary>Set by MainWindow — re-anchors the dock after dock content/settings change.</summary>
    public Action? RepositionAction { get; set; }

    /// <summary>Set by MainWindow — dismisses the window-preview popup (dock refresh).</summary>
    public Action? PreviewDismissAction { get; set; }

    public MainWindowViewModel()
    {
        LaunchCommand = new RelayCommand(_ => { });
        UpdateClock();
    }

    public MainWindowViewModel(AppServices appServices)
    {
        _appServices = appServices;
        LaunchCommand = new RelayCommand(param => ExecuteItem(param));
        _appServices.LocalizationService.AddListener(UpdateDockUI);
        _appServices.BadgeService.BadgesChanged += OnBadgesChanged;
        UpdateClock();
    }

    public void Initialize()
    {
        if (_appServices == null) return;
        ApplyAppearance();
        UpdateDockUI();
        StartIndicatorWatcher();
        StartClock();
    }

    private void StartClock()
    {
        _clockTimer.Tick += OnClockTick;
        OnClockTick(null, EventArgs.Empty);
    }

    /// <summary>
    /// Refreshes the clock and schedules the next tick just past the next
    /// second boundary: the minute flips on time, and a sleep/resume or a
    /// system clock change is picked up within a second.
    /// </summary>
    private void OnClockTick(object? sender, EventArgs e)
    {
        UpdateClock();
        _clockTimer.Stop();
        _clockTimer.Interval = TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 20);
        _clockTimer.Start();
    }

    /// <summary>Unchanged texts raise no PropertyChanged, so ticks within a minute cost no layout.</summary>
    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockTime = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        // Invariant culture: "/" in a format string is the culture's date
        // separator, which is "-" in some locales (es-CL).
        ClockDate = now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        ClockToolTip = now.ToString("D", CultureInfo.CurrentCulture);
        Calendar.UpdateToday(now);
    }

    public void UpdateDockUI()
    {
        if (_appServices == null) return;
        IsVerticalDock = _appServices.AppearanceService.GetVerticalDock();
        // The tint must be set BEFORE items are created: CreateItemViewModel
        // tints each icon immediately, and a stale value would make every
        // pinned item lag one color selection behind.
        var appearance = _appServices.AppearanceService;
        IconTinter.ActiveColor = appearance.GetTintIcons()
            ? ParseRgbColor(appearance.GetTintColorRGB())
            : null;
        UpdateBadgeWatcher();
        Items.Clear();
        var dock = _appServices.DockService.GetDock();
        var loc = _appServices.LocalizationService;

        foreach (var item in dock.Items)
        {
            var vm = CreateItemViewModel(item, loc);
            if (vm != null)
            {
                vm.IconSize = _appServices.AppearanceService.GetIconsSize();
                Items.Add(vm);
            }
        }
        ApplyAppearance();
        RepositionAction?.Invoke();
        PreviewDismissAction?.Invoke();
        // The new item view models start without badges.
        OnBadgesChanged();
    }
    // --- continued below ---

    private DockItemViewModel? CreateItemViewModel(DockItem item, LocalizationService loc)
    {
        string label = loc.DockItemLabel(item);

        if (item is DockSettingsItemModel)
        {
            var icon = IconLoader.LoadFromAsset(IconLoader.MapResourcePath(item.Path));
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockWindowsModuleItemModel moduleItem)
        {
            var icon = IconLoader.LoadWindowsModuleIcon(moduleItem.Module);
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        if (item is DockProgramItemModel programItem)
        {
            string? iconPath = _appServices!.IconGateway.ResolveProgramIcon(programItem.ExecutablePath);
            var icon = IconLoader.LoadFromFile(iconPath);
            var itemVm = new DockItemViewModel(item, label, LaunchCommand,
                showIndicator: true, executablePath: programItem.ExecutablePath)
            {
                Icon = IconTinter.Apply(icon)
            };

            // The icon may not be cached yet (first run with a new item). Extract it in the
            // background, then push the resulting bitmap into the ViewModel so the dock
            // updates without requiring a restart.
            if (icon == null)
            {
                string exe = programItem.ExecutablePath;
                _ = Task.Run(() =>
                {
                    _appServices.IconGateway.CacheProgramIcon(exe);
                    string? cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    var loaded = IconLoader.LoadFromFile(cached);
                    if (loaded != null)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => itemVm.Icon = IconTinter.Apply(loaded));
                });
            }

            return itemVm;
        }

        if (item is DockFolderItemModel folderItem)
        {
            string? iconPath = _appServices!.IconGateway.ResolveFolderIcon(folderItem.FolderPath);
            var icon = IconLoader.LoadFromFile(iconPath);
            if (icon == null)
            {
                _ = Task.Run(() => _appServices.IconGateway.CacheFolderIcon(folderItem.FolderPath));
                icon = IconLoader.LoadFromAsset("Assets/icons/folder.png");
            }
            return new DockItemViewModel(item, label, LaunchCommand) { Icon = IconTinter.Apply(icon) };
        }

        return null;
    }

    private void ApplyAppearance()
    {
        if (_appServices == null) return;
        var appearance = _appServices.AppearanceService;

        IconsSize = appearance.GetIconsSize();
        foreach (var app in RunningApps)
            app.IconSize = IconsSize;
        Spacing = appearance.GetSpacingBetweenIcons();
        BorderRounding = appearance.GetDockBorderRounding();

        // Re-tint the persistent running-apps VMs with the current tint
        // (set in UpdateDockUI before items are created; pinned items are
        // recreated on every refresh so they are tinted at creation).
        foreach (var app in RunningApps)
            app.Icon = IconTinter.Apply(app.OriginalIcon);

        string colorRgb = appearance.GetDockColorRGB();
        double transparency = appearance.GetDockTransparencyPercentage() / 100.0;
        byte alpha = (byte)(transparency * 255);
        var parts = colorRgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        DockBackground = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    private static Color ParseRgbColor(string rgb)
    {
        var parts = rgb.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        return Color.FromRgb(r, g, b);
    }

    /// <summary>
    /// Left click, taskbar-style: a running program is brought forward (or
    /// minimized when it already is the active window); anything else is
    /// launched or opened.
    /// </summary>
    private void ExecuteItem(object? param)
    {
        if (param is not DockItemViewModel itemVm || _appServices == null) return;

        if (itemVm.Item is DockProgramItemModel programItem
            && _appServices.WindowPreviewService.ActivateOrMinimize(OpenWindows(programItem.ExecutablePath, programItem.Label)))
        {
            PreviewDismissAction?.Invoke();
            return;
        }
        LaunchItem(itemVm);
    }

    // --- Context-menu actions (right click on a dock item) ---

    /// <summary>
    /// Launches the item, starting a new instance even when it is already
    /// running (taskbar: the app name entry of the menu).
    /// </summary>
    public void LaunchItem(DockItemViewModel itemVm)
    {
        if (_appServices == null) return;
        bool launched = _appServices.ItemActionService.Execute(
            itemVm.Item, () => OpenSettingsAction?.Invoke());

        if (!launched && itemVm.Item is DockProgramItemModel programItem)
        {
            var loc = _appServices.LocalizationService;
            System.Windows.Forms.MessageBox.Show(
                loc.Text("dialog.programNotFound.message",
                    programItem.Label, programItem.ExecutablePath),
                loc.Text("dialog.programNotFound.title"),
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }
    }

    /// <summary>Left click on an unpinned running app: same taskbar behavior as pinned items.</summary>
    public void ActivateRunningApp(RunningAppViewModel app)
    {
        _appServices?.WindowPreviewService.ActivateOrMinimize(OpenWindows(app.ExecutablePath, app.Label));
        PreviewDismissAction?.Invoke();
    }

    /// <summary>Starts a new instance of an unpinned running app.</summary>
    public void LaunchRunningApp(RunningAppViewModel app) =>
        _appServices?.ItemActionService.Execute(
            new DockProgramItemModel(app.Label, app.ExecutablePath), () => { });

    public void RunAsAdministrator(DockItem item) =>
        _appServices?.ItemActionService.RunAsAdministrator(item);

    public void OpenFileLocation(DockItem item) =>
        _appServices?.ItemActionService.OpenFileLocation(item);

    /// <summary>
    /// Open windows of the executable: their count drives the close entries and
    /// the first one identifies the app for its jump list.
    /// </summary>
    public List<CedroModernDock.Core.Domain.WindowInfo> OpenWindows(string? executablePath, string label)
    {
        if (_appServices == null || string.IsNullOrEmpty(executablePath)) return new();
        try
        {
            return _appServices.WindowPreviewService.LoadPreview(
                new DockProgramItemModel(label, executablePath));
        }
        catch
        {
            return new();
        }
    }

    public void CloseAllWindows(string? executablePath, string label) =>
        _appServices?.WindowPreviewService.CloseAll(executablePath, label);

    /// <summary>Removes a pinned item from the dock. Returns true when the dock changed.</summary>
    public bool UnpinItem(DockItemViewModel itemVm)
    {
        if (_appServices == null || !_appServices.DockService.UnpinItem(itemVm.Item)) return false;
        UpdateDockUI();
        return true;
    }

    /// <summary>
    /// Pins an unpinned running app, labelled with the executable's file
    /// description ("Windows Terminal") like the taskbar does. Returns true
    /// when the dock changed.
    /// </summary>
    public bool PinRunningApp(RunningAppViewModel app)
    {
        if (_appServices == null) return false;
        string label = ResolveProgramLabel(app.ExecutablePath, app.Label);
        if (!_appServices.DockService.PinProgram(label, app.ExecutablePath)) return false;
        UpdateDockUI();
        // Drop it from the running section now instead of on the next watcher tick.
        if (_runningAppsByPath.Remove(app.ExecutablePath))
        {
            RunningApps.Remove(app);
            HasRunningApps = RunningApps.Count > 0;
            RepositionAction?.Invoke();
        }
        return true;
    }

    private static string ResolveProgramLabel(string executablePath, string fallback)
    {
        try
        {
            string? description = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description.Trim();
        }
        catch { /* unreadable version info: keep the file name */ }
        return fallback;
    }

    private void StartIndicatorWatcher()
    {
        _indicatorCts?.Cancel();
        _indicatorCts = new CancellationTokenSource();
        var token = _indicatorCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    RefreshIndicators();
                    RefreshRunningApps();
                    await _refreshSignal.WaitAsync(1200, token);
                }
                catch (OperationCanceledException) { break; }
                catch { /* keep watching */ }
            }
        }, token);
    }

    private void RefreshIndicators()
    {
        if (_appServices == null) return;
        bool showBadges = _appServices.AppearanceService.GetShowTaskbarBadges();
        var programItems = Items.Where(i => i.ShowIndicator && i.ExecutablePath != null).ToList();
        foreach (var item in programItems)
        {
            var windows = _appServices.WindowPreviewService.FindOpenWindows(item.ExecutablePath, item.Label);
            bool isOpen = windows.Count > 0;
            // Packaged apps keep their badge while closed, as on the taskbar.
            TaskbarBadge? badge = showBadges
                ? _appServices.BadgeService.BadgeFor(
                    windows.Select(w => w.Handle),
                    _appServices.WindowPreviewService.AppIdsOf(item.ExecutablePath!, item.Label))
                : null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                item.IsRunning = isOpen;
                item.SetBadge(badge);
            });
        }
    }

    /// <summary>Runs the badge listeners only while the setting is on.</summary>
    private void UpdateBadgeWatcher()
    {
        if (_appServices == null) return;
        try
        {
            if (_appServices.AppearanceService.GetShowTaskbarBadges())
                _appServices.BadgeService.Start();
            else
                _appServices.BadgeService.Stop();
        }
        catch { /* badges are best-effort; the dock works without them */ }
    }

    /// <summary>Called on any thread: refreshes indicators and badges right away.</summary>
    private void OnBadgesChanged()
    {
        if (_refreshSignal.CurrentCount == 0)
        {
            try { _refreshSignal.Release(); }
            catch (SemaphoreFullException) { /* a refresh is already pending */ }
        }
    }

    private readonly Dictionary<string, RunningAppViewModel> _runningAppsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Syncs the running-apps section: taskbar-visible windows, grouped by executable,
    /// minus programs already pinned in the dock. Only touches the UI when the set
    /// actually changed, so the preview popup never flickers.
    /// </summary>
    private void RefreshRunningApps()
    {
        if (_appServices == null) return;

        // Feature toggle: only show unpinned running apps when enabled.
        if (!_appServices.AppearanceService.GetShowUnpinnedRunningApps())
        {
            if (RunningApps.Count > 0)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RunningApps.Clear();
                    _runningAppsByPath.Clear();
                    HasRunningApps = false;
                    RepositionAction?.Invoke();
                    PreviewDismissAction?.Invoke();
                });
            }
            return;
        }

        var pinnedPrograms = Items
            .Where(i => i.Item is DockProgramItemModel)
            .Select(i => (DockProgramItemModel)i.Item)
            .ToList();
        List<string> pinnedPaths = pinnedPrograms.Select(p => p.ExecutablePath).ToList();
        // Pinned items started through an alias or a launcher (wt.exe, a
        // WhatsApp launcher) own windows of another executable: the taskbar
        // groups them by AppUserModelID, and so does the dock.
        var pinnedAppIds = new HashSet<string>(
            pinnedPrograms.SelectMany(p => _appServices.WindowPreviewService.AppIdsOf(p.ExecutablePath, p.Label)),
            StringComparer.OrdinalIgnoreCase);

        List<CedroModernDock.Core.Domain.RunningWindowInfo> windows;
        try
        {
            windows = _appServices.WindowPreviewService.FindTaskbarWindows();
        }
        catch
        {
            return;
        }

        var groups = windows
            .GroupBy(w => w.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => !IsPinned(g.Key, pinnedPaths)
                && !g.Any(w => w.AppId != null && pinnedAppIds.Contains(w.AppId)))
            .ToList();
        var desired = groups.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        bool showBadges = _appServices.AppearanceService.GetShowTaskbarBadges();
        var badges = groups.ToDictionary(
            g => g.Key,
            g => showBadges
                ? _appServices.BadgeService.BadgeFor(
                    g.Select(w => w.Handle),
                    g.Select(w => w.AppId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                : null,
            StringComparer.OrdinalIgnoreCase);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_appServices == null) return;

            bool changed = false;

            // Remove apps that closed or got pinned.
            foreach (var path in _runningAppsByPath.Keys.ToList())
            {
                if (!desired.ContainsKey(path) || IsPinned(path, pinnedPaths))
                {
                    RunningApps.Remove(_runningAppsByPath[path]);
                    _runningAppsByPath.Remove(path);
                    changed = true;
                }
            }

            // Add newly opened apps.
            foreach (var (path, win) in desired)
            {
                if (_runningAppsByPath.ContainsKey(path)) continue;
                string label = System.IO.Path.GetFileNameWithoutExtension(path);
                var vm = new RunningAppViewModel(path, label)
                {
                    IconSize = _appServices.AppearanceService.GetIconsSize()
                };
                LoadRunningIcon(vm, win.Handle);
                _runningAppsByPath[path] = vm;
                RunningApps.Add(vm);
                changed = true;
            }

            foreach (var (path, badge) in badges)
            {
                if (_runningAppsByPath.TryGetValue(path, out var app))
                    app.SetBadge(badge);
            }

            if (changed)
            {
                HasRunningApps = RunningApps.Count > 0;
                RepositionAction?.Invoke();
                PreviewDismissAction?.Invoke();
            }
        });
    }

    private static bool IsPinned(string executablePath, List<string> pinnedPaths)
    {
        string file = System.IO.Path.GetFileName(executablePath);
        foreach (var pinned in pinnedPaths)
        {
            if (string.Equals(pinned, executablePath, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(System.IO.Path.GetFileName(pinned), file, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void LoadRunningIcon(RunningAppViewModel vm, IntPtr windowHandle)
    {
        if (_appServices == null) return;
        string exe = vm.ExecutablePath;

        // The actual Windows Settings app (SystemSettings.exe) carries no icon
        // in its EXE and its UWP frame exposes none either, so its dock icon is
        // hardcoded to the bundled Windows Settings asset.
        if (string.Equals(System.IO.Path.GetFileName(exe),
                "SystemSettings.exe", StringComparison.OrdinalIgnoreCase))
        {
            var settingsIcon = IconLoader.LoadFromAsset("Assets/icons/windows_settings.png");
            if (settingsIcon != null)
            {
                vm.OriginalIcon = settingsIcon;
                vm.Icon = IconTinter.Apply(settingsIcon);
                return;
            }
        }

        var icon = IconLoader.LoadFromFile(_appServices.IconGateway.ResolveProgramIcon(exe));
        if (icon != null)
        {
            vm.OriginalIcon = icon;
            vm.Icon = IconTinter.Apply(icon);
            return;
        }
        // Not cached yet — extract in the background and push when ready.
        // Modern/UWP apps (Settings, etc.) carry no icon resource in the EXE
        // and their frame window exposes none either; fall back to the running
        // window's icon, then to the AppX package manifest logo.
        _ = Task.Run(() =>
        {
            try
            {
                _appServices.IconGateway.CacheProgramIcon(exe);
                string? cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                var loaded = IconLoader.LoadFromFile(cached);
                if (loaded == null && windowHandle != IntPtr.Zero)
                {
                    WindowsIconExtractor.ExtractAndCacheWindowIcon(exe, windowHandle);
                    cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    loaded = IconLoader.LoadFromFile(cached);
                }
                if (loaded == null)
                {
                    WindowsIconExtractor.ExtractAndCacheAppxIcon(exe);
                    cached = _appServices.IconGateway.ResolveProgramIcon(exe);
                    loaded = IconLoader.LoadFromFile(cached);
                }
                if (loaded != null)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.OriginalIcon = loaded;
                        vm.Icon = IconTinter.Apply(loaded);
                    });
            }
            catch { /* best-effort */ }
        });
    }

    public void Shutdown()
    {
        if (_appServices != null)
        {
            _appServices.LocalizationService.RemoveListener(UpdateDockUI);
            _appServices.BadgeService.BadgesChanged -= OnBadgesChanged;
            _appServices.BadgeService.Stop();
        }
        _indicatorCts?.Cancel();
        _indicatorCts?.Dispose();
        _indicatorCts = null;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
    }
}
