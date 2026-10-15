namespace CedroModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using CedroModernDock.Core.Domain;

/// <summary>
/// Direct port of DefaultProgramLauncher.java. Launches executables,
/// with special handling for Discord (Squirrel-installed apps) and
/// automatic elevation (UAC) when the standard launch fails with error=740.
/// </summary>
public class WindowsProgramLauncher : IProgramLauncher
{
    private readonly Func<string, string?>? _packagedAppId;

    /// <param name="packagedAppId">AppUserModelID of the packaged app an
    /// executable belongs to, or null for desktop programs.</param>
    public WindowsProgramLauncher(Func<string, string?>? packagedAppId = null)
    {
        _packagedAppId = packagedAppId;
    }

    public bool IsPackagedApp(string executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath) && PackagedAppId(executablePath) != null;

    public bool Launch(string executablePath, string label)
    {
        Debug.WriteLine($"{label} Clicked");

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            Debug.WriteLine($"Executable path not defined for: {label}");
            return false;
        }

        try
        {
            // Packaged apps (Settings, Store apps) must be activated: started
            // directly, their exe exits at once without opening a window.
            if (PackagedAppId(executablePath) is { } appId)
                return ActivatePackagedApp(appId, label);
            return ExecuteAndHandleElevation(executablePath, label);
        }
        catch (Exception e)
        {
            // A failed launch must never take the dock down: elevation
            // (error 740) is handled inside; any other failure (missing
            // file, bad path, invalid exe) is logged and reported as a
            // failed launch.
            Debug.WriteLine($"Failed to open: {label}");
            Debug.WriteLine($"Path: {executablePath}");
            Debug.WriteLine($"Error: {e.Message}");
            return false;
        }
    }

    public bool LaunchAsAdministrator(string executablePath, string label)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        try
        {
            var launchCommand = ResolveLaunchCommand(executablePath);
            if (!File.Exists(launchCommand.ExecutablePath))
            {
                Debug.WriteLine($"Executable not found: {launchCommand.ExecutablePath}");
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = launchCommand.ExecutablePath,
                Arguments = string.Join(" ", launchCommand.Arguments.Select(a => $"\"{a}\"")),
                UseShellExecute = true,
                Verb = "runas"
            });
            Debug.WriteLine($"(Elevated) Executing: {label}");
            return true;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the UAC prompt: nothing failed.
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to open elevated: {label} ({e.Message})");
            return false;
        }
    }

    public bool OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to open file location: {path} ({e.Message})");
            return false;
        }
    }

    private string? PackagedAppId(string executablePath)
    {
        try
        {
            return _packagedAppId?.Invoke(executablePath);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Packaged app lookup failed: {executablePath} ({e.Message})");
            return null;
        }
    }

    /// <summary>Starts a packaged app the way the Start menu does, through shell:AppsFolder.</summary>
    private static bool ActivatePackagedApp(string appId, string label)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{appId}",
            UseShellExecute = true
        });
        Debug.WriteLine($"Activating packaged app: {label} ({appId})");
        return true;
    }

    /// <summary>ERROR_CANCELLED: the user dismissed the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    private bool ExecuteAndHandleElevation(string path, string label)
    {
        var launchCommand = ResolveLaunchCommand(path);
        if (!File.Exists(launchCommand.ExecutablePath))
        {
            Debug.WriteLine($"Executable not found: {launchCommand.ExecutablePath}");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launchCommand.ExecutablePath,
                UseShellExecute = false
            };
            foreach (var arg in launchCommand.Arguments)
                startInfo.ArgumentList.Add(arg);

            Process.Start(startInfo);
            Debug.WriteLine($"Executing: {label}");
            return true;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // error=740 means elevation is required (ERROR_ELEVATION_REQUIRED)
            if (e.NativeErrorCode == 740)
            {
                Debug.WriteLine("Standard execution failed. Requesting elevation...");
                string command = BuildElevationCommand(launchCommand);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command {command}",
                    UseShellExecute = false
                });
                Debug.WriteLine($"(Elevated) Executing: {label}");
                return true;
            }

            Debug.WriteLine("Error trying to execute program");
            return false;
        }
    }

    /// <summary>
    /// Resolves the actual launch command for an executable, handling
    /// Discord's Squirrel installer pattern (app runs via Update.exe --processStart).
    /// </summary>
    public static LaunchCommand ResolveLaunchCommand(string executablePath)
    {
        string normalizedPath = Path.GetFullPath(executablePath);
        if (IsDiscordExecutable(normalizedPath))
        {
            string? versionDirectory = Path.GetDirectoryName(normalizedPath);
            string? installDirectory = versionDirectory != null
                ? Path.GetDirectoryName(versionDirectory) : null;

            if (installDirectory != null)
            {
                string updateExecutable = Path.Combine(installDirectory, "Update.exe");
                if (File.Exists(updateExecutable))
                {
                    return new LaunchCommand(
                        updateExecutable,
                        new[] { "--processStart", Path.GetFileName(normalizedPath) }
                    );
                }
            }
        }

        return new LaunchCommand(normalizedPath, Array.Empty<string>());
    }

    private static bool IsDiscordExecutable(string executablePath)
    {
        string? fileName = Path.GetFileName(executablePath);
        string? versionDirectory = Path.GetDirectoryName(executablePath);
        string? installDirectory = versionDirectory != null
            ? Path.GetDirectoryName(versionDirectory) : null;

        if (fileName == null || versionDirectory == null || installDirectory == null)
            return false;

        return string.Equals(fileName, "Discord.exe", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(versionDirectory)?.StartsWith("app-", StringComparison.OrdinalIgnoreCase) == true
            && string.Equals(Path.GetFileName(installDirectory), "Discord", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildElevationCommand(LaunchCommand launchCommand)
    {
        string escapedFilePath = EscapePowerShellArgument(launchCommand.ExecutablePath);
        if (launchCommand.Arguments.Length == 0)
            return $"Start-Process -FilePath '{escapedFilePath}' -Verb RunAs";

        string escapedArguments = string.Join(", ",
            launchCommand.Arguments.Select(a => $"'{EscapePowerShellArgument(a)}'"));

        return $"Start-Process -FilePath '{escapedFilePath}' -ArgumentList {escapedArguments} -Verb RunAs";
    }

    private static string EscapePowerShellArgument(string argument) => argument.Replace("'", "''");

    /// <summary>Direct port of the Java LaunchCommand record.</summary>
    public sealed record LaunchCommand(string ExecutablePath, string[] Arguments);
}
