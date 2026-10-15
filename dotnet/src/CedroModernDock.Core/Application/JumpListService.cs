namespace CedroModernDock.Core.Application;

using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

/// <summary>
/// Builds the jump list section of a dock item's context menu the way the
/// Windows taskbar lays it out.
/// </summary>
public class JumpListService
{
    private readonly IJumpListGateway _gateway;

    public JumpListService(IJumpListGateway gateway)
    {
        _gateway = gateway;
    }

    public IReadOnlyList<JumpListCategory> Load(string executablePath, string label, IntPtr windowHandle) =>
        Arrange(_gateway.Load(executablePath, label, windowHandle), _gateway.MaxItems);

    public bool Open(JumpListEntry entry, string ownerExecutablePath) =>
        _gateway.Open(entry, ownerExecutablePath);

    public string? ResolveIcon(JumpListEntry entry) => _gateway.ResolveIcon(entry);

    /// <summary>
    /// Taskbar layout rules:
    /// <list type="bullet">
    /// <item>An app without a custom list gets the shell default: Recent.</item>
    /// <item>Categories keep their stored order, except Tasks, which go last.</item>
    /// <item>App-defined categories are shown whole; known categories
    /// (Recent/Frequent) fill the slots left up to <paramref name="maxItems"/>,
    /// always at least one, skipping destinations already shown.</item>
    /// <item>Empty categories are dropped.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<JumpListCategory> Arrange(JumpListSource source, int maxItems)
    {
        var stored = source.HasCustomList
            ? source.Categories
            : new[] { new JumpListCategory(JumpListCategoryKind.Recent, "", Array.Empty<JumpListEntry>()) };

        var ordered = stored.Where(c => c.Kind != JumpListCategoryKind.Tasks)
            .Concat(stored.Where(c => c.Kind == JumpListCategoryKind.Tasks));

        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in stored.Where(c => c.Kind == JumpListCategoryKind.Custom))
            foreach (var entry in category.Entries.Where(e => !e.IsSeparator))
                shown.Add(entry.DestinationKey);
        int slotsLeft = maxItems - shown.Count;

        var result = new List<JumpListCategory>();
        foreach (var category in ordered)
        {
            IReadOnlyList<JumpListEntry> entries = category.Kind switch
            {
                JumpListCategoryKind.Recent => TakeUnshown(source.Recent, shown, Math.Max(1, slotsLeft)),
                JumpListCategoryKind.Frequent => TakeUnshown(source.Frequent, shown, Math.Max(1, slotsLeft)),
                _ => category.Entries
            };
            if (category.Kind is JumpListCategoryKind.Recent or JumpListCategoryKind.Frequent)
                slotsLeft -= entries.Count;

            if (entries.Any(e => !e.IsSeparator))
                result.Add(category with { Entries = entries });
        }
        return result;
    }

    private static List<JumpListEntry> TakeUnshown(IEnumerable<JumpListEntry> entries, HashSet<string> shown, int count)
    {
        var taken = new List<JumpListEntry>();
        foreach (var entry in entries)
        {
            if (taken.Count >= count) break;
            if (entry.IsSeparator || !shown.Add(entry.DestinationKey)) continue;
            taken.Add(entry);
        }
        return taken;
    }
}
