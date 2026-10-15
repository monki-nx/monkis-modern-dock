namespace CedroModernDock.Core.Models;

using System.Globalization;

/// <summary>
/// What an app shows on its taskbar button when Windows' "Show badges on
/// taskbar apps" is on: a count or a glyph published as a badge notification
/// (packaged apps), or an overlay icon set through ITaskbarList3 (desktop apps
/// such as Outlook, Teams or Slack).
/// </summary>
/// <param name="Count">Unread count of a numeric badge.</param>
/// <param name="Glyph">Glyph name of a glyph badge (newMessage, alert, busy...).</param>
/// <param name="OverlayPng">PNG of the overlay icon. The instance is kept while
/// the overlay does not change, so unchanged badges compare equal.</param>
/// <param name="Description">Accessibility text of the overlay ("5 unread messages").</param>
public sealed record TaskbarBadge(
    int? Count = null,
    string? Glyph = null,
    byte[]? OverlayPng = null,
    string? Description = null)
{
    /// <summary>
    /// Parses the value of a badge notification (&lt;badge value="..."/&gt;).
    /// "none", "0" and an empty value clear the badge and return null.
    /// </summary>
    public static TaskbarBadge? FromBadgeValue(string? value)
    {
        string trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            return count > 0 ? new TaskbarBadge(Count: count) : null;
        return new TaskbarBadge(Glyph: trimmed);
    }
}
