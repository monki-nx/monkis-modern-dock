using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Views;

public partial class MainWindow : Window
{
    private DockWindowBehavior? _dockBehavior;
    private DockAppBar? _appBar;
    private AppServices? _appServices;
    private WindowPreviewPopup? _previewPopup;
    private ContextMenu? _openContextMenu;
    private IntPtr _contextMenuHwnd;
    private IntPtr _contextMenuForeground;
    private readonly DispatcherTimer _contextMenuWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Button? _hoveredButton;
    private Button? _previewAnchor;
    private int _previewRequestId;
    // Identity of the item whose preview is currently shown. _hoveredButton is
    // nulled while the pointer is over the popup, so the close handler cannot
    // rely on it; these are captured when the preview is requested.
    private string _previewExecutablePath = "";
    private string _previewLabel = "";
    private readonly DispatcherTimer _previewHideDebounce = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _previewCloseRefresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _positionPersistTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow()
    {
        InitializeComponent();
        PositionChanged += OnDockPositionChanged;
        _positionPersistTimer.Tick += (_, _) =>
        {
            _positionPersistTimer.Stop();
            PersistDockPosition();
        };
        // The popup hides only when the pointer is neither over the popup nor
        // over a native thumbnail window (thumbnails are separate top-level
        // windows, so leaving the popup onto a thumbnail fires PointerExited).
        _previewHideDebounce.Tick += (_, _) =>
        {
            _previewHideDebounce.Stop();
            if (IsPointerOverPreview())
                _previewHideDebounce.Start();
            else
                HidePreview();
        };
        // After a close request the window may take a moment to disappear (it
        // can show a save prompt), so the row refresh is deferred until it has.
        _previewCloseRefresh.Tick += (_, _) =>
        {
            _previewCloseRefresh.Stop();
            RefreshPreviewAfterClose();
        };
        _contextMenuWatch.Tick += OnContextMenuWatchTick;
    }

    private bool IsPointerOverPreview()
    {
        if (_previewPopup is not { IsVisible: true } popup) return false;
        if (!User32.GetCursorPos(out POINT pt)) return false;
        return popup.IsPointOverPopup(pt.X, pt.Y);
    }

    /// <summary>Receives the composed application services from the App composition root.</summary>
    public void SetAppServices(AppServices appServices) => _appServices = appServices;

    /// <summary>
    /// Once the native window is created and shown, grab its HWND and apply
    /// the dock-specific Win32 behavior (no-activate, no-taskbar, Win+D defense).
    /// Also applies the saved dock position from the positioning service.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        IPlatformHandle? handle = this.TryGetPlatformHandle();
        if (handle is null)
        {
            UpdateStatus("ERROR: Could not obtain native window handle");
            return;
        }

        _dockBehavior = new DockWindowBehavior(handle.Handle, UpdateStatus);
        _dockBehavior.Apply();
        _appBar = new DockAppBar(handle.Handle, _dockBehavior.AppBarCallbackMessage);
        _dockBehavior.AppBarNotification = code =>
        {
            if (code == DockAppBar.ABN_POSCHANGED)
                UpdateAppBar(force: true);
        };

        // Initialize the dock ViewModel (loads items, starts indicator watcher).
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenSettingsAction = () => OpenSettings(vm);
            vm.RepositionAction = () => ApplyDockPosition();
            vm.PreviewDismissAction = HidePreview;
            vm.Initialize();
        }

        ApplyDockPosition(force: true);

        // Static anchors must use the finalized window size, which SizeToContent
        // only produces after the first layout pass. Re-apply once layout settles
        // and whenever the dock content resizes the window. Subscribed in both
        // modes: switching to STATIC from settings must re-anchor on resizes
        // too, and the reserved screen strip follows the dock's size.
        SizeChanged += OnDockSizeChanged;
        Dispatcher.UIThread.Post(() => ApplyDockPosition(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Reserves the screen strip under the dock (appbar) when enabled and the
    /// dock is horizontal, so maximized windows stop above it; releases it
    /// otherwise. Positions and sizes are read from the native window.
    /// </summary>
    private void UpdateAppBar(bool force = false)
    {
        if (_appBar == null || _appServices == null) return;
        bool reserve = _appServices.PositioningService.GetReserveScreenSpace()
            && !_appServices.AppearanceService.GetVerticalDock();
        IntPtr hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (!reserve || hwnd == IntPtr.Zero || !User32.GetWindowRect(hwnd, out RECT rect))
        {
            _appBar.Remove();
            return;
        }
        _appBar.ReserveBottom(rect, force);
    }

    /// <summary>
    /// Applies the dock position from the positioning service. In STATIC mode
    /// this re-anchors the dock to the current screen edge; in DYNAMIC mode the
    /// saved position is only applied once at startup (force) so the user's
    /// drags are never overridden by subsequent refreshes.
    /// </summary>
    private void ApplyDockPosition(bool force = false)
    {
        if (_appServices == null) return;
        if (force || !_appServices.PositioningService.IsDynamicPositioning())
        {
            var (x, y) = _appServices.PositioningService.ResolvePosition(Width, Height);
            // The dock is parented to the desktop window, so a position set on it
            // is relative to that parent, whose origin is the virtual screen's
            // top-left (negative with a monitor above or left of the primary one),
            // while Position reads back in screen coordinates. Without this offset
            // the saved position drifts by that origin on every start.
            var (originX, originY) = _dockBehavior?.GetParentOrigin() ?? (0, 0);
            Position = new PixelPoint((int)x - originX, (int)y - originY);
        }
        UpdateAppBar();
    }

    private void OnDockSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyDockPosition();

    private void OnItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (_openContextMenu != null) return;
        if (button.DataContext is not DockItemViewModel vm || vm.Item is not DockProgramItemModel item)
            return;

        _hoveredButton = button;
        _previewHideDebounce.Stop();
        int requestId = ++_previewRequestId;
        var programItem = item;
        string label = vm.Label;
        _previewExecutablePath = programItem.ExecutablePath;
        _previewLabel = label;

        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                windows = _appServices.WindowPreviewService.LoadPreview(programItem);
            }
            catch (Exception)
            {
                windows = new List<WindowInfo>();
            }
            Dispatcher.UIThread.Post(() => OnPreviewLoaded(requestId, button, label, windows));
        });
    }

    private void OnRunningAppPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (_openContextMenu != null) return;
        if (button.DataContext is not RunningAppViewModel vm) return;

        _hoveredButton = button;
        _previewHideDebounce.Stop();
        int requestId = ++_previewRequestId;
        string executablePath = vm.ExecutablePath;
        string label = vm.Label;
        _previewExecutablePath = executablePath;
        _previewLabel = label;

        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                // Build a throwaway program item to reuse the preview loader.
                windows = _appServices.WindowPreviewService.LoadPreview(
                    new DockProgramItemModel(label, executablePath));
            }
            catch (Exception)
            {
                windows = new List<WindowInfo>();
            }
            Dispatcher.UIThread.Post(() => OnPreviewLoaded(requestId, button, label, windows));
        });
    }

    /// <summary>Middle click on a pinned item starts a new instance, as on the taskbar.</summary>
    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle) return;
        if (sender is not Button { DataContext: DockItemViewModel itemVm }) return;
        if (DataContext is not MainWindowViewModel vm || itemVm.Item is DockSettingsItemModel) return;
        e.Handled = true;
        HidePreview();
        vm.LaunchItem(itemVm);
    }

    /// <summary>Middle click on an unpinned running app starts a new instance, as on the taskbar.</summary>
    private void OnRunningAppPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle) return;
        if (sender is not Button { DataContext: RunningAppViewModel app }) return;
        if (DataContext is not MainWindowViewModel vm) return;
        e.Handled = true;
        HidePreview();
        vm.LaunchRunningApp(app);
    }

    /// <summary>
    /// Clicking an unpinned running app behaves like the taskbar (and like a
    /// pinned item): bring its window forward, minimize it when it is already
    /// active, or cycle through its windows when it has several.
    /// </summary>
    private void OnRunningAppClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel app) return;
        if (DataContext is not MainWindowViewModel vm) return;

        ++_previewRequestId;
        _previewPopup?.HideNow();
        _hoveredButton = null;
        vm.ActivateRunningApp(app);
    }

    /// <summary>
    /// Right click (or the Menu key) on a pinned item: the taskbar menu — the
    /// app's jump list (VS Code's recent folders, Terminal profiles, ...)
    /// followed by the item name (launch), elevation, file location, unpin and
    /// close. The settings item is mandatory and opens settings on click, so
    /// it has none.
    /// </summary>
    private void OnItemContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not DockItemViewModel itemVm) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (itemVm.Item is DockSettingsItemModel) return;

        var loc = _appServices.LocalizationService;
        var windows = vm.OpenWindows(itemVm.ExecutablePath, itemVm.Label);
        var entries = new List<Control>();
        if (itemVm.ExecutablePath is { } executablePath)
            AddJumpListEntries(entries, executablePath, itemVm.Label, windows);

        entries.Add(MenuEntry(itemVm.Label, () => vm.LaunchItem(itemVm), icon: itemVm.Icon));
        if (itemVm.Item is DockProgramItemModel)
            entries.Add(MenuEntry(loc.Text("contextMenu.runAsAdministrator"),
                () => vm.RunAsAdministrator(itemVm.Item), GlyphAdministrator));
        if (DockItemActionService.HasFileLocation(itemVm.Item))
            entries.Add(MenuEntry(loc.Text("contextMenu.openFileLocation"),
                () => vm.OpenFileLocation(itemVm.Item), GlyphFolder));
        entries.Add(MenuEntry(loc.Text("contextMenu.unpin"), () =>
        {
            if (vm.UnpinItem(itemVm))
                SettingsWindow.RefreshItemsIfOpen();
        }, GlyphUnpin));
        AddCloseEntry(entries, windows);

        ShowContextMenu(button, entries);
    }

    /// <summary>Right click on an unpinned running app: jump list, then launch, elevate, locate, pin, close.</summary>
    private void OnRunningAppContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || _appServices == null) return;
        if (button.DataContext is not RunningAppViewModel app) return;
        if (DataContext is not MainWindowViewModel vm) return;

        var loc = _appServices.LocalizationService;
        var programItem = new DockProgramItemModel(app.Label, app.ExecutablePath);
        var windows = vm.OpenWindows(app.ExecutablePath, app.Label);
        var entries = new List<Control>();
        AddJumpListEntries(entries, app.ExecutablePath, app.Label, windows);

        entries.Add(MenuEntry(app.Label, () => vm.LaunchRunningApp(app), icon: app.Icon));
        entries.Add(MenuEntry(loc.Text("contextMenu.runAsAdministrator"),
            () => vm.RunAsAdministrator(programItem), GlyphAdministrator));
        entries.Add(MenuEntry(loc.Text("contextMenu.openFileLocation"),
            () => vm.OpenFileLocation(programItem), GlyphFolder));
        entries.Add(MenuEntry(loc.Text("contextMenu.pin"), () =>
        {
            if (vm.PinRunningApp(app))
                SettingsWindow.RefreshItemsIfOpen();
        }, GlyphPin));
        AddCloseEntry(entries, windows);

        ShowContextMenu(button, entries);
    }

    /// <summary>
    /// Adds the app's jump list as on the taskbar: a gray header per category,
    /// its destinations, and a separator before the dock's own entries.
    /// </summary>
    private void AddJumpListEntries(List<Control> entries, string executablePath, string label,
        List<WindowInfo> windows)
    {
        if (_appServices == null) return;
        var jumpLists = _appServices.JumpListService;
        IReadOnlyList<JumpListCategory> categories;
        try
        {
            IntPtr window = windows.Count > 0 ? windows[0].Handle : IntPtr.Zero;
            categories = jumpLists.Load(executablePath, label, window);
        }
        catch (Exception)
        {
            return; // a broken jump list must never block the dock's own menu
        }
        if (categories.Count == 0) return;

        var loc = _appServices.LocalizationService;
        foreach (var category in categories)
        {
            string title = category.Kind switch
            {
                JumpListCategoryKind.Recent => loc.Text("jumpList.recent"),
                JumpListCategoryKind.Frequent => loc.Text("jumpList.frequent"),
                JumpListCategoryKind.Tasks => loc.Text("jumpList.tasks"),
                _ => category.Title
            };
            entries.Add(new MenuItem { Header = title, IsEnabled = false, FontSize = 11 });

            foreach (var entry in category.Entries)
            {
                if (entry.IsSeparator)
                {
                    entries.Add(new Separator());
                    continue;
                }
                var item = MenuEntry(entry.Title, () => jumpLists.Open(entry, executablePath),
                    icon: IconLoader.LoadFromFile(jumpLists.ResolveIcon(entry)));
                if (!entry.IsLink)
                    ToolTip.SetTip(item, entry.TargetPath);
                entries.Add(item);
            }
        }
        entries.Add(new Separator());
    }

    /// <summary>"Close window" for one open window, "Close all windows" for several, nothing when closed.</summary>
    private void AddCloseEntry(List<Control> entries, List<WindowInfo> windows)
    {
        if (_appServices is not { } services || windows.Count == 0) return;
        string key = windows.Count == 1 ? "contextMenu.closeWindow" : "contextMenu.closeAllWindows";
        entries.Add(MenuEntry(services.LocalizationService.Text(key), () =>
        {
            foreach (var window in windows)
                services.WindowPreviewService.Close(window);
        }, GlyphClose));
    }

    // Segoe Fluent Icons glyphs (Segoe MDL2 Assets fallback), as on the taskbar menu.
    private const string GlyphAdministrator = "";
    private const string GlyphFolder = "";
    private const string GlyphPin = "";
    private const string GlyphUnpin = "";
    private const string GlyphClose = "";
    private static readonly FontFamily GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static MenuItem MenuEntry(string header, Action action, string? glyph = null, IImage? icon = null)
    {
        // Long destination names are cut with an ellipsis, as on the taskbar.
        var entry = new MenuItem
        {
            Header = new TextBlock { Text = header, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        if (icon != null)
            entry.Icon = new Image { Source = icon, Width = 14, Height = 14 };
        else if (glyph != null)
            entry.Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = GlyphFont,
                FontSize = 12,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        // Run after the menu has closed: unpinning rebuilds the dock items,
        // which removes the button the menu is still anchored to.
        entry.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return entry;
    }

    /// <summary>
    /// Paints the context menu like the dock bar — its color, transparency and
    /// corner rounding — in a compact size. Text is light on dark dock colors
    /// and dark on light ones. The Fluent theme's menu brushes and paddings are
    /// overridden as resources scoped to this menu only.
    /// </summary>
    private void ApplyDockStyle(ContextMenu menu)
    {
        if (_appServices == null) return;
        var appearance = _appServices.AppearanceService;

        var parts = appearance.GetDockColorRGB()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte Channel(int i) => parts.Length > i && byte.TryParse(parts[i], out var v) ? v : (byte)0;
        byte r = Channel(0), g = Channel(1), b = Channel(2);
        byte alpha = (byte)Math.Clamp(appearance.GetDockTransparencyPercentage() / 100.0 * 255, 0, 255);

        bool darkDock = (0.299 * r + 0.587 * g + 0.114 * b) < 140;
        Color text = darkDock ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        Color Tint(byte a) => Color.FromArgb(a, text.R, text.G, text.B);

        menu.Background = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        menu.BorderBrush = new SolidColorBrush(Tint(0x30));
        menu.BorderThickness = new Thickness(1);
        // Windows 11 menu corners: the acrylic backdrop fills the popup's
        // rectangle and DWM rounds it to this radius (see ShowContextMenu).
        menu.CornerRadius = new CornerRadius(MenuCornerRadius);
        menu.Padding = new Thickness(3);
        menu.FontSize = 12;
        // Subpixel (ClearType) text blends glyph edges with what the window
        // itself draws behind them; over the transparent acrylic background
        // that leaves a dark fringe around the letters, most visible over
        // light content. Grayscale antialiasing keeps the text clean.
        RenderOptions.SetTextRenderingMode(menu, TextRenderingMode.Antialias);

        var resources = menu.Resources;
        resources["MenuFlyoutItemForeground"] = new SolidColorBrush(text);
        resources["MenuFlyoutItemForegroundPointerOver"] = new SolidColorBrush(text);
        resources["MenuFlyoutItemForegroundPressed"] = new SolidColorBrush(text);
        resources["MenuFlyoutItemForegroundDisabled"] = new SolidColorBrush(Tint(0x99));
        resources["MenuFlyoutItemBackgroundPointerOver"] = new SolidColorBrush(Tint(0x26));
        resources["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(Tint(0x18));
        resources["MenuFlyoutItemBackgroundDisabled"] = Brushes.Transparent;
        resources["SystemControlForegroundBaseMediumLowBrush"] = new SolidColorBrush(Tint(0x33));
        resources["MenuFlyoutItemThemePaddingNarrow"] = new Thickness(8, 4, 10, 5);
        resources["MenuFlyoutSeparatorThemePadding"] = new Thickness(-3, 2, -3, 2);
        resources["MenuIconPresenterMargin"] = new Thickness(0, 0, 10, 0);
    }

    private const int DwmWindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int DwmCornerRound = 2;             // DWMWCP_ROUND (8 px, Windows 11)
    private const double MenuCornerRadius = 8;

    private void ShowContextMenu(Control anchor, List<Control> entries)
    {
        // The taskbar drops the preview at once when its menu opens (no fade).
        ++_previewRequestId;
        _previewPopup?.HideNow();
        _hoveredButton = null;
        _openContextMenu?.Close();

        bool vertical = _appServices?.AppearanceService.GetVerticalDock() == true;
        var menu = new ContextMenu
        {
            // Outside the dock bar, like the taskbar menu. The popup flips to
            // the opposite side when the screen edge leaves no room.
            Placement = vertical ? PlacementMode.Right : PlacementMode.Top,
            ItemsSource = entries
        };
        ApplyDockStyle(menu);
        menu.Closed += (_, _) =>
        {
            if (_openContextMenu != menu) return;
            _openContextMenu = null;
            _contextMenuHwnd = IntPtr.Zero;
            _contextMenuWatch.Stop();
        };
        _openContextMenu = menu;
        menu.Open(anchor);

        // The dock lives in the desktop layer, and so would its popups: a
        // window covering the area above the dock would hide the menu. Keep
        // it topmost, like the taskbar menu.
        var popupRoot = TopLevel.GetTopLevel(menu);
        _contextMenuHwnd = popupRoot?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_contextMenuHwnd != IntPtr.Zero)
            User32.SetWindowPos(_contextMenuHwnd, Win32Constants.HWND_TOPMOST, 0, 0, 0, 0,
                Win32Constants.SWP_NOMOVE | Win32Constants.SWP_NOSIZE | Win32Constants.SWP_NOACTIVATE);

        // Windows 11 acrylic: the system blurs what is behind the menu and
        // tints it with the dock's color and transparency; the menu's own
        // background then steps aside so the tint is not applied twice. When
        // the system refuses acrylic the dock-colored background stays. DWM
        // rounds the popup window so the blur follows the menu's corners.
        if (_contextMenuHwnd != IntPtr.Zero)
        {
            DwmThumbnailInterop.SetWindowAttribute(_contextMenuHwnd, DwmWindowCornerPreference, DwmCornerRound);
            // The tint's opacity is the "menu acrylic intensity" setting: lower
            // values let more of the blurred background show through.
            byte intensity = (byte)Math.Round(
                (_appServices?.AppearanceService.GetMenuAcrylicIntensity() ?? 40) / 100.0 * 255);
            if (menu.Background is ISolidColorBrush { Color: var tint }
                && AcrylicBackdrop.Enable(_contextMenuHwnd, intensity, tint.R, tint.G, tint.B))
                menu.Background = Brushes.Transparent;
        }

        _contextMenuForeground = User32.GetForegroundWindow();
        _contextMenuWatch.Start();
    }

    /// <summary>
    /// Light dismiss for the context menu. The dock is WS_EX_NOACTIVATE and
    /// parented to the desktop, so it never becomes the active window and
    /// Avalonia never sees the deactivation that normally closes a menu when
    /// the user clicks another app. Poll instead: any mouse button pressed
    /// outside the menu, Escape, or a foreground change closes it.
    /// </summary>
    private void OnContextMenuWatchTick(object? sender, EventArgs e)
    {
        if (_openContextMenu is not { } menu)
        {
            _contextMenuWatch.Stop();
            return;
        }

        bool buttonDown = IsKeyDown(Win32Constants.VK_LBUTTON)
            || IsKeyDown(Win32Constants.VK_RBUTTON)
            || IsKeyDown(Win32Constants.VK_MBUTTON);
        if ((buttonDown && !IsPointerOverContextMenu())
            || IsKeyDown(Win32Constants.VK_ESCAPE)
            || User32.GetForegroundWindow() != _contextMenuForeground)
        {
            menu.Close();
        }
    }

    private static bool IsKeyDown(int virtualKey) =>
        (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private bool IsPointerOverContextMenu()
    {
        if (_contextMenuHwnd == IntPtr.Zero) return false;
        if (!User32.GetCursorPos(out POINT pt)) return false;
        if (!User32.GetWindowRect(_contextMenuHwnd, out RECT rect)) return false;
        return pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
    }

    private void OnPreviewLoaded(int requestId, Button button, string label,
        List<WindowInfo> windows)
    {
        if (requestId != _previewRequestId || _hoveredButton != button) return;
        if (windows.Count == 0)
        {
            if (_previewPopup?.IsVisible == true)
                HidePreview();
            return;
        }
        if (_appServices == null) return;
        _previewAnchor = button;

        var appearance = _appServices.AppearanceService;
        if (_previewPopup == null)
        {
            _previewPopup = new WindowPreviewPopup();
            _previewPopup.ThumbnailClicked = hwnd => OnPopupThumbnailClicked(hwnd);
            _previewPopup.CloseRequested = hwnd => OnPopupCloseRequested(hwnd);
            _previewPopup.PointerEnteredCallback = OnPopupPointerEntered;
            _previewPopup.PointerExitedCallback = OnPopupPointerExited;
        }
        // Re-hovering the same item (e.g. after visiting the popup) must not
        // tear down and rebuild the live thumbnails: that races with the hide
        // debounce and can leave rows blank. Keep the already-shown popup.
        if (_previewPopup.IsVisible && _previewPopup.MatchesSource(windows))
        {
            _previewPopup.CancelHide();
            return;
        }
        _previewPopup.ShowFor(windows, label,
            appearance.GetDockColorRGB(), appearance.GetDockBorderRounding(),
            appearance.GetDockTransparencyPercentage() / 100.0, button,
            verticalDock: _appServices.AppearanceService.GetVerticalDock(),
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor());
    }

    private void OnItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (_hoveredButton == sender) _hoveredButton = null;
        _previewHideDebounce.Stop();
        // During fast icon-to-icon moves the previous icon's Exit can be
        // delivered AFTER the next icon's Enter. Re-arming the hide timer then
        // hides the popup 80ms later (the cursor is over the new icon, not the
        // popup) and the requestId bump drops the pending preview load — the
        // popup would never show again until the pointer leaves and re-enters.
        if (_hoveredButton == null)
            _previewHideDebounce.Start();
    }

    private void HidePreview()
    {
        ++_previewRequestId;
        _previewPopup?.HidePopup();
        _hoveredButton = null;
    }

    private void OnPopupPointerEntered()
    {
        _previewHideDebounce.Stop();
        // Pointer returned during a fade-out: reverse it instead of hiding.
        _previewPopup?.CancelHide();
    }

    private void OnPopupPointerExited()
    {
        _previewHideDebounce.Stop();
        // Returning to a dock item (pointer now over the button) fires this
        // after OnItemPointerEntered already re-stopped the hide timer. Don't
        // re-arm it in that case: the item's own pointer handling owns the
        // hide decision, and re-arming here hides the popup 90ms later even
        // though the cursor is over the very item that shows the preview.
        if (_hoveredButton == null)
            _previewHideDebounce.Start();
    }

    private void OnPopupThumbnailClicked(IntPtr sourceHwnd)
    {
        // Click-to-activate: hide instantly (no fade), release the pointer
        // capture held by the pressed row, then bring the window forward.
        ++_previewRequestId;
        _previewPopup?.HideNow();
        _hoveredButton = null;
        if (sourceHwnd != IntPtr.Zero)
            _appServices?.WindowPreviewService.Activate(new WindowInfo(sourceHwnd, ""));
    }

    /// <summary>
    /// Close button clicked: ask the source window to close (WM_CLOSE, the same
    /// message the taskbar sends) and defer a row refresh so the popup reflects
    /// the window actually disappearing.
    /// </summary>
    private void OnPopupCloseRequested(IntPtr sourceHwnd)
    {
        if (sourceHwnd == IntPtr.Zero || _appServices == null) return;
        _appServices.WindowPreviewService.Close(new WindowInfo(sourceHwnd, ""));
        _previewCloseRefresh.Stop();
        _previewCloseRefresh.Start();
    }

    /// <summary>
    /// Re-queries the shown item's open windows after a close request: if any
    /// remain, the popup rows are rebuilt without the closed window; if the
    /// closed window was the last one, the whole popup hides. Runs on the UI
    /// thread with the source query executed off it.
    /// </summary>
    private void RefreshPreviewAfterClose()
    {
        if (_appServices == null || _previewPopup == null) return;
        if (_previewPopup.IsVisible != true) return;

        string executablePath = _previewExecutablePath;
        if (string.IsNullOrEmpty(executablePath)) return;

        int requestId = ++_previewRequestId;
        string label = _previewLabel;
        Task.Run(() =>
        {
            List<WindowInfo> windows;
            try
            {
                windows = _appServices.WindowPreviewService.LoadPreview(
                    new DockProgramItemModel(label, executablePath));
            }
            catch (Exception)
            {
                windows = new List<WindowInfo>();
            }
            Dispatcher.UIThread.Post(() => OnPreviewRefreshed(requestId, label, windows));
        });
    }

    private void OnPreviewRefreshed(int requestId, string label, List<WindowInfo> windows)
    {
        if (requestId != _previewRequestId || _previewPopup is not { IsVisible: true }) return;
        if (_previewPopup == null) return;
        if (windows.Count == 0)
        {
            ++_previewRequestId;
            _previewPopup.HideNow();
            _hoveredButton = null;
            return;
        }
        if (_appServices == null) return;
        // Rebuild the popup without the closed window. The pointer is over the
        // popup, so it stays open; CancelHide guards against an in-flight fade.
        var anchor = _previewAnchor ?? _hoveredButton;
        if (anchor == null) return;
        _previewPopup.CancelHide();
        var appearance = _appServices.AppearanceService;
        _previewPopup.ShowFor(windows, label,
            appearance.GetDockColorRGB(), appearance.GetDockBorderRounding(),
            appearance.GetDockTransparencyPercentage() / 100.0, anchor,
            verticalDock: _appServices.AppearanceService.GetVerticalDock(),
            horizontalAnchor: _appServices.PositioningService.GetHorizontalAnchor());
    }

    /// <summary>Allows dragging the borderless dock window (DYNAMIC mode only).</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Don't start a window drag when pressing a dock item button — the
        // button must receive the click. Drag only when pressing the dock
        // background itself.
        if (!e.Pointer.IsPrimary)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() != null)
            return;
        // In STATIC mode the dock is anchored to the screen and must not be
        // dragged; dragging is only meaningful in DYNAMIC mode.
        if (_appServices == null || !_appServices.PositioningService.IsDynamicPositioning())
            return;
        BeginMoveDrag(e);
    }

    /// <summary>
    /// PositionChanged fires for every move — including each WM_MOVE delivered
    /// while the OS move-drag loop runs. The drag blocks the UI thread, so a
    /// DispatcherTimer restart here only ticks after the drag ends, persisting
    /// the final position exactly once instead of on every pixel of the drag.
    /// </summary>
    private void OnDockPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_appServices == null) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        _positionPersistTimer.Stop();
        _positionPersistTimer.Start();
    }

    private void PersistDockPosition()
    {
        if (_appServices == null) return;
        if (!_appServices.PositioningService.IsDynamicPositioning()) return;
        _appServices.DockService.SetDockPosition(Position.X, Position.Y);
        UpdateAppBar();
    }

    private void UpdateStatus(string status)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusText = status;
    }

    /// <summary>Opens the settings window. Port of App.java openSettingsWindow().</summary>
    private void OpenSettings(MainWindowViewModel vm)
    {
        if (_appServices == null) return;
        SettingsWindow.Open(
            _appServices,
            this,
            dockRefreshAction: vm.UpdateDockUI,
            positioningModeChangeAction: mode => HandlePositioningModeChange(mode)
        );
    }

    /// <summary>Port of App.java handlePositioningModeChange().</summary>
    private void HandlePositioningModeChange(DockPositioningMode mode)
    {
        if (_appServices == null) return;
        var currentMode = _appServices.PositioningService.GetPositioningMode();
        // Keep the current spot: DYNAMIC starts from it, and STATIC anchors to
        // the screen containing it instead of jumping to the primary screen.
        if (currentMode != mode)
        {
            _appServices.DockService.SetDockPosition(Position.X, Position.Y);
        }
        _appServices.PositioningService.SetPositioningMode(mode);
    }

    protected override void OnClosed(EventArgs e)
    {
        PositionChanged -= OnDockPositionChanged;
        _positionPersistTimer.Stop();
        _contextMenuWatch.Stop();
        _openContextMenu?.Close();
        HidePreview();
        if (DataContext is MainWindowViewModel vm)
            vm.Shutdown();
        // Give the reserved screen strip back before the window goes away.
        _appBar?.Dispose();
        _appBar = null;
        _dockBehavior?.Dispose();
        _dockBehavior = null;
        base.OnClosed(e);
    }
}