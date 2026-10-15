using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;

namespace CedroModernDock.Infrastructure.Windows.Adapters;

/// <summary>
/// Taskbar badges on Windows: overlay icons of desktop apps, seen through the
/// <see cref="TaskbandInterceptor"/>, and badge notifications of packaged
/// apps, read by the <see cref="NotificationBadgeReader"/>.
/// </summary>
public sealed class WindowsTaskbarBadgeGateway : ITaskbarBadgeGateway, IDisposable
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<IntPtr, byte[]> _overlayIcons = new();
    private readonly ConcurrentDictionary<IntPtr, string> _overlayDescriptions = new();
    private TaskbandInterceptor? _interceptor;
    private NotificationBadgeReader? _reader;

    public event Action? BadgesChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (_interceptor != null) return;

            _interceptor = new TaskbandInterceptor();
            _interceptor.OverlayIconSet += OnOverlayIconSet;
            _interceptor.OverlayDescriptionSet += OnOverlayDescriptionSet;
            _interceptor.Rehooked += RequestCurrentOverlays;
            if (_interceptor.Start())
                RequestCurrentOverlays();

            _reader = new NotificationBadgeReader();
            _reader.Changed += RaiseBadgesChanged;
            _reader.Start();

            // The taskband property must go back to Explorer even when the
            // dock exits without stopping the badges first.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_interceptor == null) return;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _interceptor.Dispose();
            _interceptor = null;
            _reader?.Dispose();
            _reader = null;
            _overlayIcons.Clear();
            _overlayDescriptions.Clear();
        }
        RaiseBadgesChanged();
    }

    public void Dispose() => Stop();

    public IReadOnlyDictionary<IntPtr, TaskbarBadge> WindowOverlays()
    {
        var overlays = new Dictionary<IntPtr, TaskbarBadge>();
        foreach (var (hwnd, png) in _overlayIcons)
        {
            if (!User32.IsWindow(hwnd))
            {
                _overlayIcons.TryRemove(hwnd, out _);
                _overlayDescriptions.TryRemove(hwnd, out _);
                continue;
            }
            _overlayDescriptions.TryGetValue(hwnd, out var description);
            overlays[hwnd] = new TaskbarBadge(OverlayPng: png, Description: description);
        }
        return overlays;
    }

    public IReadOnlyDictionary<string, TaskbarBadge> AppBadges() =>
        _reader?.Current ?? new Dictionary<string, TaskbarBadge>();

    private void OnProcessExit(object? sender, EventArgs e) => Stop();

    /// <summary>Apps resend the overlay they show when their taskbar button is (re)created.</summary>
    private static void RequestCurrentOverlays() =>
        TaskbandInterceptor.RequestButtonRefresh(Win32WindowQuery.GetTaskbarWindows().Select(w => w.Handle));

    private void OnOverlayIconSet(IntPtr hwnd, IntPtr icon)
    {
        // Converted right away: the app may destroy the icon once the call returns.
        byte[]? png = icon == IntPtr.Zero ? null : IconToPng(icon);
        if (png == null)
        {
            if (!_overlayIcons.TryRemove(hwnd, out _)) return;
            _overlayDescriptions.TryRemove(hwnd, out _);
        }
        else
        {
            _overlayIcons[hwnd] = png;
        }
        RaiseBadgesChanged();
    }

    private void OnOverlayDescriptionSet(IntPtr hwnd, string? description)
    {
        if (description == null)
            _overlayDescriptions.TryRemove(hwnd, out _);
        else
            _overlayDescriptions[hwnd] = description;
        if (_overlayIcons.ContainsKey(hwnd))
            RaiseBadgesChanged();
    }

    private void RaiseBadgesChanged()
    {
        try { BadgesChanged?.Invoke(); }
        catch { /* listeners must not break the interceptor thread */ }
    }

    private static byte[]? IconToPng(IntPtr icon)
    {
        try
        {
            using var managed = Icon.FromHandle(icon);
            using var bitmap = managed.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
