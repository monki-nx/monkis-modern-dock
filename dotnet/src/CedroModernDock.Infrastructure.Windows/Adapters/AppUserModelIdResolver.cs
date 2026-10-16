namespace CedroModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Finds the AppUserModelID(s) a dock item may be registered under. Jump lists
/// are stored per AppUserModelID, which a program usually sets on itself at
/// runtime and which cannot be derived from the executable path alone.
/// Candidates are tried in order; the caller keeps the first one that has a
/// jump list on disk.
/// </summary>
public sealed class AppUserModelIdResolver
{
    private const string ExplorerAppId = "Microsoft.Windows.Explorer";
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ItemIdsLifetime = TimeSpan.FromMinutes(1);

    private readonly object _cacheLock = new();
    private readonly Dictionary<IntPtr, (uint ProcessId, string? AppId)> _windowIds = new();
    private readonly Dictionary<string, (DateTime LoadedAt, string[] AppIds)> _itemIds = new(StringComparer.OrdinalIgnoreCase);

    // Known folders the shell substitutes by their GUID when it derives an
    // implicit AppUserModelID from an executable path.
    private static readonly (Environment.SpecialFolder? Folder, string? Path, string Guid)[] KnownFolders =
    {
        (null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), "{5CD7AEE2-2219-4A67-B85D-6C9CE15660CB}"),
        (Environment.SpecialFolder.ProgramFiles, null, "{6D809377-6AF0-444B-8957-A3773F02200E}"),
        (Environment.SpecialFolder.ProgramFilesX86, null, "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"),
        (Environment.SpecialFolder.System, null, "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"),
        (Environment.SpecialFolder.SystemX86, null, "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"),
        (Environment.SpecialFolder.Windows, null, "{F38BF404-1D43-42F2-9305-67DE0B28FC23}"),
        (Environment.SpecialFolder.LocalApplicationData, null, "{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"),
    };

    private readonly object _catalogLock = new();
    private List<AppEntry>? _catalog;
    private DateTime _catalogLoadedAt;

    private sealed record AppEntry(string AppId, string TargetPath, string DisplayName, string InstallPath);

    /// <summary>Loads the Start menu app catalog ahead of the first right click.</summary>
    public void Prewarm() => GetCatalog();

    /// <summary>
    /// The AppUserModelID of the packaged app an executable belongs to (an exe
    /// inside a package's install folder, like Settings' SystemSettings.exe),
    /// or null for ordinary desktop programs and execution aliases. Packaged
    /// apps start through their AppUserModelID; running the exe directly
    /// exits without opening a window.
    /// </summary>
    public string? PackagedAppId(string executablePath)
    {
        var catalog = GetCatalog();
        var appIds = AppIdsInstalledAt(executablePath, catalog.Select(app => (app.AppId, app.InstallPath)));
        if (appIds.Count <= 1)
            return appIds.FirstOrDefault();

        // A package with several apps: its manifest says which one runs this exe.
        string installPath = catalog.First(app => app.AppId == appIds[0]).InstallPath;
        return AppIdFromManifest(executablePath, installPath, appIds) ?? appIds[0];
    }

    /// <summary>
    /// The exe a catalog app runs: its package install path plus the
    /// manifest's &lt;Application Executable&gt; for the app's Id; null when
    /// the app is not in shell:AppsFolder or has no package. The same path
    /// the app's CoreWindow reports, so a minimized UWP frame resolved this
    /// way groups with its restored windows.
    /// </summary>
    public string? ExecutableOf(string appId)
    {
        var app = GetCatalog().FirstOrDefault(a => string.Equals(a.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (app == null || string.IsNullOrEmpty(app.InstallPath)) return null;
        string id = appId[(appId.LastIndexOf('!') + 1)..];
        try
        {
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(app.InstallPath, "AppxManifest.xml"));
            string? executable = manifest.Descendants()
                .Where(e => e.Name.LocalName == "Application")
                .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("Executable")?.Value;
            return executable == null ? null : Path.GetFullPath(Path.Combine(app.InstallPath, executable.Replace('/', '\\')));
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Package manifest lookup failed for {appId}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// AppUserModelIDs of the catalog apps whose package install folder holds
    /// <paramref name="executablePath"/>, deepest folder first.
    /// </summary>
    public static IReadOnlyList<string> AppIdsInstalledAt(string executablePath,
        IEnumerable<(string AppId, string InstallPath)> apps)
    {
        string full;
        try
        {
            full = Path.GetFullPath(executablePath);
        }
        catch
        {
            return [];
        }

        return apps
            .Where(app => !string.IsNullOrEmpty(app.InstallPath)
                && full.StartsWith(app.InstallPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.InstallPath.TrimEnd('\\').Length)
            .Select(app => app.AppId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Picks the app whose &lt;Application Executable="..."&gt; in the package
    /// manifest is <paramref name="executablePath"/>; AppUserModelIDs are
    /// "&lt;package family&gt;!&lt;Application Id&gt;".
    /// </summary>
    private static string? AppIdFromManifest(string executablePath, string installPath, IReadOnlyList<string> appIds)
    {
        try
        {
            string relative = Path.GetRelativePath(installPath, Path.GetFullPath(executablePath));
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(installPath, "AppxManifest.xml"));
            foreach (var application in manifest.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                string? executable = application.Attribute("Executable")?.Value;
                string? id = application.Attribute("Id")?.Value;
                if (id == null || executable == null
                    || !string.Equals(executable.Replace('/', '\\'), relative, StringComparison.OrdinalIgnoreCase))
                    continue;
                string? match = appIds.FirstOrDefault(appId =>
                    appId.EndsWith("!" + id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Package manifest lookup failed: {e.Message}");
        }
        return null;
    }

    /// <summary>
    /// The AppUserModelID the taskbar groups a window under: the window's own
    /// property, else its packaged process's ID. Cached per window (and
    /// process, since handles are recycled); null for ordinary desktop
    /// processes that set none on the window.
    /// </summary>
    public string? WindowAppId(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        lock (_cacheLock)
        {
            if (_windowIds.TryGetValue(hwnd, out var cached) && cached.ProcessId == pid)
                return cached.AppId;
        }

        string? appId = FromWindow(hwnd) ?? FromProcess(hwnd);
        lock (_cacheLock)
        {
            if (_windowIds.Count > 512)
            {
                foreach (var stale in _windowIds.Keys.Where(h => !User32.IsWindow(h)).ToList())
                    _windowIds.Remove(stale);
            }
            _windowIds[hwnd] = (pid, appId);
        }
        return appId;
    }

    /// <summary>
    /// AppUserModelIDs a pinned item's windows can carry: the Start menu
    /// shortcut's ID, its package's apps, or the Start menu app of the same
    /// name. Unlike <see cref="Candidates"/> it leaves out the implicit
    /// path-derived IDs, which never appear on windows.
    /// </summary>
    public IReadOnlyCollection<string> ItemAppIds(string executablePath, string? label)
    {
        string key = executablePath + "|" + label;
        lock (_cacheLock)
        {
            if (_itemIds.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.LoadedAt < ItemIdsLifetime)
                return cached.AppIds;
        }

        var implicitIds = new HashSet<string>(ImplicitAppIds(executablePath), StringComparer.OrdinalIgnoreCase);
        string[] ids = Candidates(executablePath, label ?? "", IntPtr.Zero)
            .Where(id => !implicitIds.Contains(id))
            .ToArray();
        lock (_cacheLock)
            _itemIds[key] = (DateTime.UtcNow, ids);
        return ids;
    }

    public IEnumerable<string> Candidates(string executablePath, string label, IntPtr windowHandle)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? id in EnumerateCandidates(executablePath, label, windowHandle))
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                yield return id;
        }
    }

    private IEnumerable<string?> EnumerateCandidates(string executablePath, string label, IntPtr windowHandle)
    {
        // 1. A running window states its AppUserModelID explicitly.
        if (windowHandle != IntPtr.Zero)
        {
            yield return FromWindow(windowHandle);
            yield return FromProcess(windowHandle);
        }

        string fileName = Path.GetFileName(executablePath);
        if (string.Equals(fileName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
            yield return ExplorerAppId;

        var catalog = GetCatalog();

        // 2. Start menu shortcuts carry the AppUserModelID of the program they target.
        foreach (var app in catalog)
        {
            if (string.Equals(app.TargetPath, executablePath, StringComparison.OrdinalIgnoreCase))
                yield return app.AppId;
        }

        // 3. Packaged apps: an execution alias or an exe inside the package
        //    folder identifies the package family; its apps are "<family>!<app>".
        string? family = PackageFamilyFromPath(executablePath);
        if (family != null)
        {
            foreach (var app in catalog)
            {
                if (app.AppId.StartsWith(family + "!", StringComparison.OrdinalIgnoreCase))
                    yield return app.AppId;
            }
        }

        //    System apps live outside WindowsApps (Settings in
        //    C:\Windows\ImmersiveControlPanel): match the package install folder.
        foreach (string appId in AppIdsInstalledAt(executablePath,
                     catalog.Select(app => (app.AppId, app.InstallPath))))
            yield return appId;

        // 4. Same name as a Start menu app (covers launchers and renamed items).
        if (!string.IsNullOrWhiteSpace(label))
        {
            foreach (var app in catalog)
            {
                if (string.Equals(app.DisplayName, label, StringComparison.OrdinalIgnoreCase))
                    yield return app.AppId;
            }
        }

        // 5. Implicit AppUserModelID the shell derives from the executable path.
        foreach (string id in ImplicitAppIds(executablePath))
            yield return id;
    }

    private static string? FromWindow(IntPtr hwnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (JumpListNative.SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store) != 0)
                return null;
            try
            {
                return JumpListNative.GetString(store, JumpListNative.PKEY_AppUserModel_ID);
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"AppUserModelID from window failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Packaged processes report their AppUserModelID directly.</summary>
    private static string? FromProcess(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        IntPtr process = Kernel32.OpenProcess(Win32Constants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            uint length = 256;
            var buffer = new StringBuilder((int)length);
            return JumpListNative.GetApplicationUserModelId(process, ref length, buffer) == 0
                ? buffer.ToString()
                : null;
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    /// <summary>
    /// "%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe" (alias) or
    /// "C:\Program Files\WindowsApps\Name_1.2.3.0_x64__publisher\app.exe"
    /// -> "Name_publisher".
    /// </summary>
    internal static string? PackageFamilyFromPath(string executablePath)
    {
        string full;
        try
        {
            full = Path.GetFullPath(executablePath);
        }
        catch
        {
            return null;
        }

        string aliasRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        string? directory = Path.GetDirectoryName(full);
        if (directory != null && string.Equals(directory.TrimEnd('\\'), aliasRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Root aliases have a twin inside "<family>\" holding the same file name.
            try
            {
                foreach (string familyDir in Directory.EnumerateDirectories(aliasRoot))
                {
                    if (File.Exists(Path.Combine(familyDir, Path.GetFileName(full))))
                        return Path.GetFileName(familyDir);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Alias lookup failed: {e.Message}");
            }
            return null;
        }
        if (directory != null && string.Equals(Path.GetDirectoryName(directory)?.TrimEnd('\\'), aliasRoot, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(directory);

        // Package install folder: Name_Version_Architecture_ResourceId_PublisherId.
        foreach (string part in full.Split('\\'))
        {
            string[] fields = part.Split('_');
            if (fields.Length == 5 && full.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                return $"{fields[0]}_{fields[4]}";
        }
        return null;
    }

    internal static IEnumerable<string> ImplicitAppIds(string executablePath)
    {
        foreach (var (folder, explicitPath, guid) in KnownFolders)
        {
            string? root = explicitPath ?? (folder.HasValue ? Environment.GetFolderPath(folder.Value) : null);
            if (string.IsNullOrEmpty(root)) continue;
            root = root.TrimEnd('\\') + "\\";
            if (executablePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                yield return guid + "\\" + executablePath[root.Length..];
        }
        yield return executablePath;
    }

    private List<AppEntry> GetCatalog()
    {
        lock (_catalogLock)
        {
            if (_catalog != null && DateTime.UtcNow - _catalogLoadedAt < CatalogLifetime)
                return _catalog;
            _catalog = LoadCatalog();
            _catalogLoadedAt = DateTime.UtcNow;
            return _catalog;
        }
    }

    /// <summary>Enumerates shell:AppsFolder (the Start menu's "All apps").</summary>
    private static List<AppEntry> LoadCatalog()
    {
        var result = new List<AppEntry>();
        try
        {
            var iidItem = typeof(IShellItem2).GUID;
            if (JumpListNative.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iidItem, out IShellItem2 folder) != 0)
                return result;

            var bhid = JumpListNative.BHID_EnumItems;
            var iidEnum = typeof(IEnumShellItems).GUID;
            folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out IntPtr enumPtr);
            var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(enumPtr);
            Marshal.Release(enumPtr);

            while (items.Next(1, out IShellItem2 item, out uint fetched) == 0 && fetched == 1)
            {
                try
                {
                    var iidStore = typeof(IPropertyStore).GUID;
                    if (item.GetPropertyStore(0, ref iidStore, out IPropertyStore store) != 0) continue;
                    string? appId = JumpListNative.GetString(store, JumpListNative.PKEY_AppUserModel_ID);
                    string target = JumpListNative.GetString(store, JumpListNative.PKEY_Link_TargetParsingPath) ?? "";
                    string installPath = JumpListNative.GetString(store, JumpListNative.PKEY_AppUserModel_PackageInstallPath) ?? "";
                    Marshal.ReleaseComObject(store);
                    if (string.IsNullOrEmpty(appId)) continue;

                    string name = JumpListNative.GetDisplayName((IShellItem)item, JumpListNative.SIGDN_NORMALDISPLAY) ?? "";
                    result.Add(new AppEntry(appId, target, name, installPath));
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(folder);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"AppsFolder enumeration failed: {e.Message}");
        }
        return result;
    }
}
