namespace CedroModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;
using Microsoft.Win32;

/// <summary>
/// Reads other applications' jump lists from the files the shell keeps under
/// %APPDATA%\Microsoft\Windows\Recent:
/// <list type="bullet">
/// <item><c>CustomDestinations\&lt;id&gt;.customDestinations-ms</c> — the
/// categories and tasks the app set through ICustomDestinationList.</item>
/// <item><c>AutomaticDestinations\&lt;id&gt;.automaticDestinations-ms</c> — the
/// shell's recent/frequent lists, read through IApplicationDocumentLists.</item>
/// </list>
/// <c>&lt;id&gt;</c> is a CRC-64 of the app's AppUserModelID (see <see cref="AppIdHash"/>).
/// </summary>
public sealed class WindowsJumpListGateway : IJumpListGateway
{
    private const uint CustomDestinationsVersion = 2;
    private const uint CategoryFooter = 0xBABFFBAB;
    private const int KnownCategoryFrequent = 1;
    private const int KnownCategoryRecent = 2;
    private const int DocumentListSize = 20;
    private const int IconSize = 32;

    private readonly AppUserModelIdResolver _appIds = new();
    private readonly string _recentDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");

    public int MaxItems
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (key?.GetValue("JumpListItems_Maximum") is int value && value > 0)
                    return value;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"JumpListItems_Maximum read failed: {e.Message}");
            }
            return 10;
        }
    }

    /// <summary>Loads the Start menu app catalog ahead of the first right click.</summary>
    public void Prewarm() => _appIds.Prewarm();

    public JumpListSource Load(string executablePath, string label, IntPtr windowHandle)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return JumpListSource.Empty;

        foreach (string appId in _appIds.Candidates(executablePath, label, windowHandle))
        {
            string hash = AppIdHash(appId);
            string customPath = Path.Combine(_recentDirectory, "CustomDestinations", hash + ".customDestinations-ms");
            string automaticPath = Path.Combine(_recentDirectory, "AutomaticDestinations", hash + ".automaticDestinations-ms");
            bool hasCustom = File.Exists(customPath);
            if (!hasCustom && !File.Exists(automaticPath)) continue;

            try
            {
                var categories = hasCustom ? ReadCustomDestinations(File.ReadAllBytes(customPath)) : new List<JumpListCategory>();
                return new JumpListSource(hasCustom, categories,
                    ReadDocumentList(appId, JumpListNative.ADLT_RECENT),
                    ReadDocumentList(appId, JumpListNative.ADLT_FREQUENT));
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Jump list of {appId} unreadable: {e.Message}");
                return JumpListSource.Empty;
            }
        }
        return JumpListSource.Empty;
    }

    /// <summary>
    /// The shell names jump list files after a reflected CRC-64 (polynomial
    /// 0x92C64265D32139A4, initial value all ones, no final xor) of the
    /// upper-cased AppUserModelID in UTF-16LE, as 16 lowercase hex digits.
    /// </summary>
    public static string AppIdHash(string appId)
    {
        ulong crc = ulong.MaxValue;
        foreach (byte b in Encoding.Unicode.GetBytes(appId.ToUpperInvariant()))
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x92C64265D32139A4UL : crc >> 1;
        }
        return crc.ToString("x16");
    }

    /// <summary>
    /// Parses a customDestinations-ms file: a header (version, category count,
    /// reserved) followed by categories, each closed by a 0xBABFFBAB footer.
    /// Category types: 0 = custom (UTF-16 name + entries), 1 = known
    /// (1 Frequent, 2 Recent), 2 = tasks (entries). Each entry is a CLSID
    /// followed by the object's IPersistStream data, so the shell's own
    /// deserializer is used to read it and to learn its length.
    /// </summary>
    public static List<JumpListCategory> ReadCustomDestinations(byte[] data)
    {
        var categories = new List<JumpListCategory>();
        var reader = new BinaryReader(new MemoryStream(data), Encoding.Unicode);
        if (data.Length < 12 || reader.ReadUInt32() != CustomDestinationsVersion)
            return categories;
        uint count = reader.ReadUInt32();
        reader.ReadUInt32(); // reserved

        for (uint c = 0; c < count; c++)
        {
            uint type = reader.ReadUInt32();
            switch (type)
            {
                case 0:
                {
                    int nameLength = reader.ReadUInt16();
                    string name = Encoding.Unicode.GetString(reader.ReadBytes(nameLength * 2));
                    var entries = ReadEntries(data, reader);
                    categories.Add(new JumpListCategory(JumpListCategoryKind.Custom, name, entries));
                    break;
                }
                case 1:
                {
                    int known = reader.ReadInt32();
                    var kind = known == KnownCategoryFrequent ? JumpListCategoryKind.Frequent : JumpListCategoryKind.Recent;
                    if (known is KnownCategoryFrequent or KnownCategoryRecent)
                        categories.Add(new JumpListCategory(kind, "", Array.Empty<JumpListEntry>()));
                    break;
                }
                case 2:
                    categories.Add(new JumpListCategory(JumpListCategoryKind.Tasks, "", ReadEntries(data, reader)));
                    break;
                default:
                    return categories; // unknown layout: keep what was read
            }

            if (reader.ReadUInt32() != CategoryFooter)
                return categories;
        }
        return categories;
    }

    private static List<JumpListEntry> ReadEntries(byte[] data, BinaryReader reader)
    {
        uint count = reader.ReadUInt32();
        var entries = new List<JumpListEntry>();
        for (uint i = 0; i < count; i++)
        {
            var clsid = new Guid(reader.ReadBytes(16));
            long start = reader.BaseStream.Position;
            var (entry, consumed) = DeserializeEntry(clsid, data, (int)start);
            reader.BaseStream.Position = start + consumed;
            if (entry != null) entries.Add(entry);
        }
        return entries;
    }

    private static (JumpListEntry? Entry, long Consumed) DeserializeEntry(Guid clsid, byte[] data, int offset)
    {
        byte[] rest = data.AsSpan(offset).ToArray();
        IStream stream = JumpListNative.SHCreateMemStream(rest, (uint)rest.Length)
            ?? throw new InvalidDataException("SHCreateMemStream failed");
        Type type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        object instance = Activator.CreateInstance(type)!;
        try
        {
            ((IPersistStream)instance).Load(stream);
            IntPtr position = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                stream.Seek(0, JumpListNative.STREAM_SEEK_CUR, position);
                return (ToEntry(instance), Marshal.ReadInt64(position));
            }
            finally
            {
                Marshal.FreeHGlobal(position);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(instance);
            Marshal.ReleaseComObject(stream);
        }
    }

    /// <summary>Converts a shell link or shell item into an entry.</summary>
    private static JumpListEntry? ToEntry(object instance)
    {
        string title = "";
        bool separator = false;
        if (instance is IPropertyStore store)
        {
            title = JumpListNative.GetString(store, JumpListNative.PKEY_Title) ?? "";
            separator = JumpListNative.GetBool(store, JumpListNative.PKEY_AppUserModel_IsDestListSeparator);
        }
        if (separator)
            return new JumpListEntry("", "", IsSeparator: true);

        if (instance is IShellLinkW link)
        {
            var buffer = new StringBuilder(2048);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            string target = buffer.ToString();
            buffer.Clear();
            link.GetArguments(buffer, buffer.Capacity);
            string arguments = buffer.ToString();
            buffer.Clear();
            link.GetWorkingDirectory(buffer, buffer.Capacity);
            string workingDirectory = buffer.ToString();
            buffer.Clear();
            link.GetIconLocation(buffer, buffer.Capacity, out int iconIndex);
            string iconPath = Environment.ExpandEnvironmentVariables(buffer.ToString());
            if (string.IsNullOrEmpty(title))
            {
                buffer.Clear();
                link.GetDescription(buffer, buffer.Capacity);
                title = buffer.Length > 0 ? buffer.ToString() : Path.GetFileNameWithoutExtension(target);
            }
            if (string.IsNullOrEmpty(target)) return null;
            return new JumpListEntry(title, target, arguments, workingDirectory, iconPath, iconIndex, IsLink: true);
        }

        if (instance is IShellItem item)
        {
            string? path = JumpListNative.GetDisplayName(item, JumpListNative.SIGDN_FILESYSPATH)
                ?? JumpListNative.GetDisplayName(item, JumpListNative.SIGDN_DESKTOPABSOLUTEPARSING);
            if (string.IsNullOrEmpty(path)) return null;
            string name = JumpListNative.GetDisplayName(item, JumpListNative.SIGDN_NORMALDISPLAY) ?? Path.GetFileName(path);
            return new JumpListEntry(string.IsNullOrEmpty(title) ? name : title, path, IsLink: false);
        }
        return null;
    }

    /// <summary>The shell's recent or frequent destinations for the AppUserModelID.</summary>
    private static List<JumpListEntry> ReadDocumentList(string appId, int listType)
    {
        var entries = new List<JumpListEntry>();
        try
        {
            var lists = (IApplicationDocumentLists)Activator.CreateInstance(
                Type.GetTypeFromCLSID(JumpListNative.CLSID_ApplicationDocumentLists, throwOnError: true)!)!;
            try
            {
                lists.SetAppID(appId);
                var iidArray = typeof(IObjectArray).GUID;
                lists.GetList(listType, DocumentListSize, ref iidArray, out object arrayObject);
                var array = (IObjectArray)arrayObject;
                array.GetCount(out uint count);
                var iidUnknown = JumpListNative.IID_IUnknown;
                for (uint i = 0; i < count; i++)
                {
                    array.GetAt(i, ref iidUnknown, out object element);
                    try
                    {
                        var entry = ToEntry(element);
                        if (entry != null) entries.Add(entry);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine($"Jump list destination skipped: {e.Message}");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(element);
                    }
                }
                Marshal.ReleaseComObject(array);
            }
            finally
            {
                Marshal.ReleaseComObject(lists);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Document list of {appId} unreadable: {e.Message}");
        }
        return entries;
    }

    public bool Open(JumpListEntry entry, string ownerExecutablePath)
    {
        if (entry.IsSeparator || string.IsNullOrEmpty(entry.TargetPath)) return false;
        try
        {
            ProcessStartInfo startInfo;
            if (entry.IsLink)
            {
                startInfo = new ProcessStartInfo(entry.TargetPath, entry.Arguments) { UseShellExecute = true };
                if (Directory.Exists(entry.WorkingDirectory))
                    startInfo.WorkingDirectory = entry.WorkingDirectory;
            }
            else if (File.Exists(ownerExecutablePath))
            {
                // The taskbar opens a recent document with the app whose jump
                // list it belongs to, not with the file's default handler.
                startInfo = new ProcessStartInfo(ownerExecutablePath, $"\"{entry.TargetPath}\"") { UseShellExecute = true };
            }
            else
            {
                startInfo = new ProcessStartInfo(entry.TargetPath) { UseShellExecute = true };
            }
            Process.Start(startInfo);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to open jump list entry '{entry.Title}': {e.Message}");
            return false;
        }
    }

    public string? ResolveIcon(JumpListEntry entry)
    {
        if (entry.IsSeparator) return null;
        string? iconFile = ResolveIconFile(entry.IconPath);
        bool fromIconLocation = iconFile != null;
        string key = fromIconLocation ? $"{iconFile},{entry.IconIndex}" : entry.TargetPath;
        if (string.IsNullOrEmpty(key)) return null;

        string cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CedroModernDock", "iconsCache", $"jumplist_{CacheKey(key)}.png");
        if (File.Exists(cachePath)) return cachePath;

        try
        {
            IntPtr icon = fromIconLocation
                ? ExtractIcon(iconFile!, entry.IconIndex)
                : ShellIcon(entry.TargetPath);
            if (icon == IntPtr.Zero && entry.IsLink && File.Exists(entry.TargetPath))
                icon = ExtractIcon(entry.TargetPath, 0);
            if (icon == IntPtr.Zero) return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                using var managed = System.Drawing.Icon.FromHandle(icon);
                using var bitmap = managed.ToBitmap();
                bitmap.Save(cachePath, ImageFormat.Png);
            }
            finally
            {
                User32Icon.DestroyIcon(icon);
            }
            return File.Exists(cachePath) ? cachePath : null;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Jump list icon failed for '{entry.Title}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves a link's icon file. Apps may store a bare file name (VS Code's
    /// recent folders use "explorer.exe,0", the folder icon), which the shell
    /// looks up in the system folders and on PATH.
    /// </summary>
    public static string? ResolveIconFile(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        if (Path.IsPathRooted(iconPath))
            return File.Exists(iconPath) ? iconPath : null;

        var directories = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (string directory in directories)
        {
            try
            {
                string candidate = Path.Combine(directory.Trim(), iconPath);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // malformed PATH entry
            }
        }
        return null;
    }

    private static IntPtr ExtractIcon(string path, int index)
    {
        var icons = new IntPtr[1];
        var ids = new int[1];
        int extracted = User32Icon.PrivateExtractIcons(path, index, IconSize, IconSize, icons, ids, 1, 0);
        return extracted > 0 ? icons[0] : IntPtr.Zero;
    }

    /// <summary>The icon Explorer shows for a file or folder (its type icon).</summary>
    private static IntPtr ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        IntPtr result = Shell32.SHGetFileInfoW(path, 0, ref info, Marshal.SizeOf<SHFILEINFO>(), Shell32.SHGFI_ICON);
        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    private static string CacheKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).ToLowerInvariant();
}
