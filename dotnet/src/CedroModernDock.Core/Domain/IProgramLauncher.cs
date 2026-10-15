namespace CedroModernDock.Core.Domain;

/// <summary>Port for launching executable programs (.exe).</summary>
public interface IProgramLauncher
{
    /// <summary>
    /// Launches the program. Returns true when the launch was attempted
    /// successfully, false when the executable could not be launched
    /// (e.g. it no longer exists).
    /// </summary>
    bool Launch(string executablePath, string label);

    /// <summary>
    /// Launches the program elevated (UAC prompt). Returns true when the launch
    /// was attempted or the user dismissed the prompt, false when the
    /// executable could not be launched.
    /// </summary>
    bool LaunchAsAdministrator(string executablePath, string label);

    /// <summary>
    /// Opens the folder containing the given file or folder with the item
    /// selected (taskbar "Open file location"). Returns false when the path
    /// no longer exists.
    /// </summary>
    bool OpenFileLocation(string path);

    /// <summary>
    /// True when the executable belongs to a packaged (Store/UWP) app, such as
    /// Windows Settings: it starts through its AppUserModelID, cannot be run
    /// elevated, and its file location means nothing to the user.
    /// </summary>
    bool IsPackagedApp(string executablePath);
}
