using System.Xml.Linq;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Native;
using Timer = System.Threading.Timer;

namespace CedroModernDock.Infrastructure.Windows.Adapters;

/// <summary>
/// Reads the badge notifications packaged apps publish (BadgeUpdateManager)
/// from the Windows notification platform's database, the store the taskbar
/// reads them from. There is no public API to read another app's badge.
/// <para>
/// The database (%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db,
/// WAL mode) is opened read-only on the original file; a copy would miss the
/// rows still in the WAL. Changes are picked up through a file watcher, with a
/// slow poll as a safety net because the WAL is not always reported promptly.
/// </para>
/// </summary>
public sealed class NotificationBadgeReader : IDisposable
{
    private const string Query = """
        SELECT h.PrimaryId, n.Payload, n.ExpiryTime
        FROM Notification n JOIN NotificationHandler h ON h.RecordId = n.HandlerId
        WHERE n.Type = 'badge'
        ORDER BY n.ArrivalTime
        """;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(10);

    private readonly string _databasePath;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private IReadOnlyDictionary<string, TaskbarBadge> _current = EmptyBadges;

    private static readonly IReadOnlyDictionary<string, TaskbarBadge> EmptyBadges =
        new Dictionary<string, TaskbarBadge>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised on a thread-pool thread when the set of badges changes.</summary>
    public event Action? Changed;

    public NotificationBadgeReader(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Notifications", "wpndatabase.db");
    }

    /// <summary>Badges by AppUserModelID.</summary>
    public IReadOnlyDictionary<string, TaskbarBadge> Current => _current;

    public void Start()
    {
        lock (_lock)
        {
            if (_timer != null) return;
            _timer = new Timer(_ => Reload(), null, TimeSpan.Zero, Poll);

            string? folder = Path.GetDirectoryName(_databasePath);
            if (folder == null || !Directory.Exists(folder)) return;
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(_databasePath) + "*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
            _timer?.Dispose();
            _timer = null;
            _current = EmptyBadges;
        }
    }

    public void Dispose() => Stop();

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Every read touches the -shm index: reacting to it would loop.
        if (e.Name != null && e.Name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)) return;
        lock (_lock)
        {
            // Restart the poll as a one-shot debounce; the regular period resumes after it.
            _timer?.Change(Debounce, Poll);
        }
    }

    private void Reload()
    {
        IReadOnlyDictionary<string, TaskbarBadge> badges;
        try
        {
            badges = ReadDatabase();
        }
        catch
        {
            return; // locked or unreadable: keep the last known badges
        }

        lock (_lock)
        {
            if (_timer == null || SameBadges(_current, badges)) return;
            _current = badges;
        }
        Changed?.Invoke();
    }

    private IReadOnlyDictionary<string, TaskbarBadge> ReadDatabase()
    {
        if (!File.Exists(_databasePath)) return EmptyBadges;
        using var db = WinSqlite.OpenReadOnly(_databasePath, busyTimeoutMs: 1000);
        if (db == null) return EmptyBadges;

        var rows = new List<BadgeRow>();
        db.Query(Query, row => rows.Add(new BadgeRow(
            row.Text(0) ?? "",
            row.IsNull(1) ? [] : row.Bytes(1),
            row.IsNull(2) ? 0 : row.Int64(2))));
        return ParseBadges(rows, DateTime.UtcNow);
    }

    /// <summary>A badge row of the notification database.</summary>
    /// <param name="AppId">AppUserModelID of the app (NotificationHandler.PrimaryId).</param>
    /// <param name="Payload">UTF-8 badge XML, e.g. &lt;badge value="3"/&gt;.</param>
    /// <param name="ExpiryTime">FILETIME (UTC) after which the badge is gone; 0 for never.</param>
    public sealed record BadgeRow(string AppId, byte[] Payload, long ExpiryTime);

    /// <summary>
    /// Current badge of each app from rows ordered by arrival: the latest row
    /// of an app wins, and expired or cleared badges ("none", "0") drop out.
    /// </summary>
    public static IReadOnlyDictionary<string, TaskbarBadge> ParseBadges(IEnumerable<BadgeRow> rows, DateTime nowUtc)
    {
        long now = nowUtc.ToFileTimeUtc();
        var latest = new Dictionary<string, TaskbarBadge?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.AppId)) continue;
            bool expired = row.ExpiryTime > 0 && row.ExpiryTime <= now;
            latest[row.AppId] = expired ? null : TaskbarBadge.FromBadgeValue(BadgeValue(row.Payload));
        }

        var badges = new Dictionary<string, TaskbarBadge>(StringComparer.OrdinalIgnoreCase);
        foreach (var (appId, badge) in latest)
        {
            if (badge != null) badges[appId] = badge;
        }
        return badges;
    }

    private static string? BadgeValue(byte[] payload)
    {
        if (payload.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(payload);
            var root = XDocument.Load(stream).Root;
            return root?.Name.LocalName == "badge" ? root.Attribute("value")?.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SameBadges(IReadOnlyDictionary<string, TaskbarBadge> a, IReadOnlyDictionary<string, TaskbarBadge> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (appId, badge) in a)
        {
            if (!b.TryGetValue(appId, out var other) || !badge.Equals(other)) return false;
        }
        return true;
    }
}
