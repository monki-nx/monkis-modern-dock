namespace CedroModernDock.Core.Domain;

using CedroModernDock.Core.Models;

/// <summary>
/// Port for reading and opening other applications' jump lists (the menu the
/// Windows taskbar shows on right click).
/// </summary>
public interface IJumpListGateway
{
    /// <summary>Most destinations the taskbar shows per jump list (the shell's JumpListItems_Maximum).</summary>
    int MaxItems { get; }

    /// <summary>
    /// Loads the jump list of the program. <paramref name="label"/> and
    /// <paramref name="windowHandle"/> (zero when not running) help identify
    /// the application. Returns <see cref="JumpListSource.Empty"/> when it has none.
    /// </summary>
    JumpListSource Load(string executablePath, string label, IntPtr windowHandle);

    /// <summary>Opens a destination; documents open with <paramref name="ownerExecutablePath"/>.</summary>
    bool Open(JumpListEntry entry, string ownerExecutablePath);

    /// <summary>Returns a cached PNG of the destination's icon, or null when none can be extracted.</summary>
    string? ResolveIcon(JumpListEntry entry);
}
