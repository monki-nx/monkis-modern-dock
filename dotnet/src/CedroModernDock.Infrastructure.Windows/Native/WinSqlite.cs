using System.Runtime.InteropServices;
using System.Text;

namespace CedroModernDock.Infrastructure.Windows.Native;

/// <summary>
/// Minimal read-only access to SQLite through winsqlite3.dll, the copy of
/// SQLite that ships with Windows (System32), so the dock carries no SQLite
/// package or native binary of its own.
/// </summary>
internal sealed class WinSqlite : IDisposable
{
    private const string Lib = "winsqlite3.dll";
    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_OPEN_READONLY = 0x00000001;
    private const int SQLITE_NULL = 5;

    private IntPtr _db;

    private WinSqlite(IntPtr db)
    {
        _db = db;
    }

    /// <summary>Opens a database read-only. Returns null when it cannot be opened.</summary>
    public static WinSqlite? OpenReadOnly(string path, int busyTimeoutMs)
    {
        byte[] utf8Path = Encoding.UTF8.GetBytes(path + "\0");
        if (sqlite3_open_v2(utf8Path, out IntPtr db, SQLITE_OPEN_READONLY, IntPtr.Zero) != SQLITE_OK)
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            return null;
        }
        sqlite3_busy_timeout(db, busyTimeoutMs);
        return new WinSqlite(db);
    }

    /// <summary>Runs a query and hands each row to <paramref name="readRow"/>.</summary>
    public void Query(string sql, Action<Row> readRow)
    {
        if (sqlite3_prepare16_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero) != SQLITE_OK)
            throw new InvalidOperationException("SQLite could not prepare the query.");
        try
        {
            var row = new Row(stmt);
            while (sqlite3_step(stmt) == SQLITE_ROW)
                readRow(row);
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    public void Dispose()
    {
        if (_db == IntPtr.Zero) return;
        sqlite3_close_v2(_db);
        _db = IntPtr.Zero;
    }

    /// <summary>The current row of a running query.</summary>
    public readonly struct Row
    {
        private readonly IntPtr _stmt;

        internal Row(IntPtr stmt)
        {
            _stmt = stmt;
        }

        public bool IsNull(int column) => sqlite3_column_type(_stmt, column) == SQLITE_NULL;

        public long Int64(int column) => sqlite3_column_int64(_stmt, column);

        public string? Text(int column)
        {
            IntPtr text = sqlite3_column_text16(_stmt, column);
            return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text);
        }

        /// <summary>The column's bytes (a BLOB, or a TEXT value as UTF-8).</summary>
        public byte[] Bytes(int column)
        {
            IntPtr data = sqlite3_column_blob(_stmt, column);
            int length = sqlite3_column_bytes(_stmt, column);
            var bytes = new byte[length];
            if (data != IntPtr.Zero && length > 0)
                Marshal.Copy(data, bytes, 0, length);
            return bytes;
        }
    }

    [DllImport(Lib)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Lib)]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport(Lib)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport(Lib, CharSet = CharSet.Unicode)]
    private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int nBytes, out IntPtr stmt, IntPtr tail);

    [DllImport(Lib)]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Lib)]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Lib)]
    private static extern int sqlite3_column_type(IntPtr stmt, int column);

    [DllImport(Lib)]
    private static extern long sqlite3_column_int64(IntPtr stmt, int column);

    [DllImport(Lib)]
    private static extern IntPtr sqlite3_column_text16(IntPtr stmt, int column);

    [DllImport(Lib)]
    private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int column);

    [DllImport(Lib)]
    private static extern int sqlite3_column_bytes(IntPtr stmt, int column);
}
