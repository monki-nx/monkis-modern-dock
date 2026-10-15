namespace CedroModernDock.Core.Domain;

using CedroModernDock.Core.Models;

/// <summary>
/// Port for the badges apps publish for their taskbar buttons: overlay icons
/// per window (desktop apps) and badge notifications per AppUserModelID
/// (packaged apps).
/// </summary>
public interface ITaskbarBadgeGateway
{
    /// <summary>Raised (on any thread) when an overlay or a badge changes.</summary>
    event Action? BadgesChanged;

    /// <summary>Starts listening. Calling it while started does nothing.</summary>
    void Start();

    /// <summary>Stops listening and leaves the system as it was. Calling it while stopped does nothing.</summary>
    void Stop();

    /// <summary>Overlay icons of the windows that currently have one.</summary>
    IReadOnlyDictionary<IntPtr, TaskbarBadge> WindowOverlays();

    /// <summary>Current badge notifications, keyed by AppUserModelID.</summary>
    IReadOnlyDictionary<string, TaskbarBadge> AppBadges();
}
