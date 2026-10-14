namespace CedroModernDock.Core.Models;

/// <summary>
/// Kind of a jump list category, mirroring the taskbar right-click menu:
/// categories defined by the app ("Recent Folders"), the shell-maintained
/// Recent and Frequent lists, and the app's Tasks ("New Window").
/// </summary>
public enum JumpListCategoryKind
{
    Custom,
    Recent,
    Frequent,
    Tasks
}

/// <summary>
/// One jump list destination. Links run <see cref="TargetPath"/> with
/// <see cref="Arguments"/> (e.g. VS Code's <c>--folder-uri</c>); documents
/// (<see cref="IsLink"/> false) are opened with the owning program. Entries of
/// packaged (Store) apps set <see cref="PackagedAppId"/>: they activate that
/// app with <see cref="Arguments"/> as its activation context (WhatsApp's
/// pinned chats).
/// </summary>
public sealed record JumpListEntry(
    string Title,
    string TargetPath,
    string Arguments = "",
    string WorkingDirectory = "",
    string IconPath = "",
    int IconIndex = 0,
    bool IsLink = true,
    bool IsSeparator = false,
    string PackagedAppId = "")
{
    /// <summary>Identity used to skip a destination already shown in an earlier category.</summary>
    public string DestinationKey => $"{PackagedAppId}\u0001{TargetPath}\u0001{Arguments}".ToUpperInvariant();
}

/// <summary>A titled group of jump list entries. Known categories carry no title of their own.</summary>
public sealed record JumpListCategory(
    JumpListCategoryKind Kind,
    string Title,
    IReadOnlyList<JumpListEntry> Entries);

/// <summary>
/// An application's jump list as stored by Windows: the app-defined categories
/// in stored order (known Recent/Frequent categories appear as empty
/// placeholders where the app placed them) plus the shell's recent and
/// frequent destination lists used to fill those placeholders.
/// </summary>
public sealed record JumpListSource(
    bool HasCustomList,
    IReadOnlyList<JumpListCategory> Categories,
    IReadOnlyList<JumpListEntry> Recent,
    IReadOnlyList<JumpListEntry> Frequent)
{
    public static JumpListSource Empty { get; } = new(false,
        Array.Empty<JumpListCategory>(), Array.Empty<JumpListEntry>(), Array.Empty<JumpListEntry>());
}
