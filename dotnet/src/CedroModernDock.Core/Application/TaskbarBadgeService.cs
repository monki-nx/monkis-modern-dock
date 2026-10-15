namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>
/// Resolves the badge a dock icon shows, the way the taskbar does for its
/// buttons: the overlay icon of the app's most recent window that has one,
/// otherwise the badge notification published for the app's AppUserModelID.
/// </summary>
public class TaskbarBadgeService
{
    private readonly ITaskbarBadgeGateway _gateway;

    public TaskbarBadgeService(ITaskbarBadgeGateway gateway)
    {
        _gateway = gateway;
    }

    public event Action? BadgesChanged
    {
        add => _gateway.BadgesChanged += value;
        remove => _gateway.BadgesChanged -= value;
    }

    public void Start() => _gateway.Start();

    public void Stop() => _gateway.Stop();

    /// <param name="windows">The app's windows, topmost first.</param>
    /// <param name="appIds">AppUserModelIDs the app is grouped under. A
    /// package family name matches every app of that package.</param>
    public TaskbarBadge? BadgeFor(IEnumerable<IntPtr> windows, IEnumerable<string> appIds)
    {
        var overlays = _gateway.WindowOverlays();
        if (overlays.Count > 0)
        {
            foreach (var window in windows)
            {
                if (overlays.TryGetValue(window, out var overlay))
                    return overlay;
            }
        }

        var badges = _gateway.AppBadges();
        if (badges.Count == 0) return null;

        var ids = appIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
        foreach (var id in ids)
        {
            if (badges.TryGetValue(id, out var badge))
                return badge;
        }
        // AppUserModelIDs of packaged apps are "<package family>!<app id>".
        foreach (var (appId, badge) in badges)
        {
            foreach (var id in ids)
            {
                if (appId.Length > id.Length
                    && appId[id.Length] == '!'
                    && appId.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                    return badge;
            }
        }
        return null;
    }
}
