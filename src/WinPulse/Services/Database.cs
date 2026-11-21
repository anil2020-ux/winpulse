using System.Globalization;
using Microsoft.Data.Sqlite;
using WinPulse.Core;
using WinPulse.Models;

namespace WinPulse.Services;

/// <summary>
/// Local SQLite index. One connection guarded by a lock - the indexer writes in
/// large batches and the UI reads small pages, so this stays fast and simple.
/// </summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public Database(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();
        pragma.CommandText = "PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        pragma.CommandText = "PRAGMA busy_timeout=8000;";
        pragma.ExecuteNonQuery();
        pragma.CommandText = "PRAGMA temp_store=MEMORY;";
        pragma.ExecuteNonQuery();
        pragma.CommandText = "PRAGMA cache_size=-65536;";
        pragma.ExecuteNonQuery();
    }

    public void Initialize(bool seedDefaults = true)
    {
        lock (_gate)
        {
            Execute(@"
CREATE TABLE IF NOT EXISTS roots (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    path      TEXT NOT NULL COLLATE NOCASE UNIQUE,
    label     TEXT NOT NULL DEFAULT '',
    enabled   INTEGER NOT NULL DEFAULT 1,
    sort      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS files (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    path          TEXT NOT NULL COLLATE NOCASE UNIQUE,
    name          TEXT NOT NULL,
    root          TEXT NOT NULL COLLATE NOCASE,
    root_label    TEXT NOT NULL DEFAULT '',
    ext           TEXT NOT NULL DEFAULT '',
    category      TEXT NOT NULL DEFAULT 'Other',
    is_screenshot INTEGER NOT NULL DEFAULT 0,
    size          INTEGER NOT NULL DEFAULT 0,
    created       TEXT NOT NULL,
    modified      TEXT NOT NULL,
    first_seen    TEXT NOT NULL,
    last_seen     TEXT NOT NULL,
    is_deleted    INTEGER NOT NULL DEFAULT 0,
    deleted_at    TEXT,
    is_protected  INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_files_root     ON files(root);
CREATE INDEX IF NOT EXISTS idx_files_modified ON files(modified);
CREATE INDEX IF NOT EXISTS idx_files_size     ON files(size);
CREATE INDEX IF NOT EXISTS idx_files_first    ON files(first_seen);
CREATE INDEX IF NOT EXISTS idx_files_category ON files(category);
CREATE INDEX IF NOT EXISTS idx_files_deleted  ON files(is_deleted);
CREATE INDEX IF NOT EXISTS idx_files_ext      ON files(ext);

CREATE TABLE IF NOT EXISTS events (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    kind     TEXT NOT NULL,
    path     TEXT NOT NULL,
    name     TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT 'Other',
    size     INTEGER NOT NULL DEFAULT 0,
    at       TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_at ON events(at);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE IF NOT EXISTS disk_history (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    at          TEXT NOT NULL,
    free_bytes  INTEGER NOT NULL,
    total_bytes INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_disk_at ON disk_history(at);
");

            if (seedDefaults)
            {
                SeedDefaultRoots();
            }
        }
    }

    private void SeedDefaultRoots()
    {
        var count = ScalarLong("SELECT COUNT(*) FROM roots");
        if (count > 0)
        {
            return;
        }

        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO roots (path, label, enabled, sort) VALUES (@p, @l, 1, @s)";
        var p = cmd.Parameters.Add("@p", SqliteType.Text);
        var l = cmd.Parameters.Add("@l", SqliteType.Text);
        var s = cmd.Parameters.Add("@s", SqliteType.Integer);

        var sort = 0;
        foreach (var (label, path) in KnownFolders.DefaultRoots())
        {
            p.Value = path;
            l.Value = label;
            s.Value = sort++;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ---------------------------------------------------------------- roots

    public List<RootFolder> GetRoots()
    {
        lock (_gate)
        {
            var list = new List<RootFolder>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT r.id, r.path, r.label, r.enabled,
       (SELECT COUNT(*) FROM files f WHERE f.root = r.path AND f.is_deleted = 0),
       (SELECT COALESCE(SUM(f.size), 0) FROM files f WHERE f.root = r.path AND f.is_deleted = 0)
FROM roots r
ORDER BY r.sort, r.label";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RootFolder
                {
                    Id = reader.GetInt64(0),
                    Path = reader.GetString(1),
                    Label = reader.GetString(2),
                    Enabled = reader.GetInt64(3) != 0,
                    FileCount = (int)reader.GetInt64(4),
                    TotalSize = reader.GetInt64(5),
                });
            }

            return list;
        }
    }

    public RootFolder AddRoot(string path, string label)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO roots (path, label, enabled, sort)
VALUES (@p, @l, 1, (SELECT COALESCE(MAX(sort), 0) + 1 FROM roots))
ON CONFLICT(path) DO UPDATE SET enabled = 1, label = excluded.label";
            cmd.Parameters.AddWithValue("@p", path);
            cmd.Parameters.AddWithValue("@l", label);
            cmd.ExecuteNonQuery();
            return new RootFolder { Path = path, Label = label, Enabled = true };
        }
    }

    public void RemoveRoot(long id)
    {
        lock (_gate)
        {
            string? path = null;
            using (var lookup = _connection.CreateCommand())
            {
                lookup.CommandText = "SELECT path FROM roots WHERE id = @id";
                lookup.Parameters.AddWithValue("@id", id);
                path = lookup.ExecuteScalar() as string;
            }

            if (path is null)
            {
                return;
            }

            using var tx = _connection.BeginTransaction();
            using (var delFiles = _connection.CreateCommand())
            {
                delFiles.Transaction = tx;
                delFiles.CommandText = "DELETE FROM files WHERE root = @p";
                delFiles.Parameters.AddWithValue("@p", path);
                delFiles.ExecuteNonQuery();
            }

            using (var delRoot = _connection.CreateCommand())
            {
                delRoot.Transaction = tx;
                delRoot.CommandText = "DELETE FROM roots WHERE id = @id";
                delRoot.Parameters.AddWithValue("@id", id);
                delRoot.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void SetRootEnabled(long id, bool enabled)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE roots SET enabled = @e WHERE id = @id";
            cmd.Parameters.AddWithValue("@e", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------------------------------------------------------- files

    public void UpsertFiles(IReadOnlyList<FileRecord> records, string scanStart)
    {
        if (records.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO files (path, name, root, root_label, ext, category, is_screenshot, size, created, modified, first_seen, last_seen, is_deleted, is_protected)
VALUES (@path, @name, @root, @root_label, @ext, @category, @shot, @size, @created, @modified, @first, @last, 0, 0)
ON CONFLICT(path) DO UPDATE SET
    name          = excluded.name,
    root          = excluded.root,
    root_label    = excluded.root_label,
    ext           = excluded.ext,
    category      = excluded.category,
    is_screenshot = excluded.is_screenshot,
    size          = excluded.size,
    created       = excluded.created,
    modified      = excluded.modified,
    last_seen     = excluded.last_seen,
    first_seen    = CASE WHEN files.is_deleted = 1 THEN excluded.first_seen ELSE files.first_seen END,
    is_deleted    = 0,
    deleted_at    = NULL";

            var pPath = cmd.Parameters.Add("@path", SqliteType.Text);
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pRoot = cmd.Parameters.Add("@root", SqliteType.Text);
            var pRootLabel = cmd.Parameters.Add("@root_label", SqliteType.Text);
            var pExt = cmd.Parameters.Add("@ext", SqliteType.Text);
            var pCategory = cmd.Parameters.Add("@category", SqliteType.Text);
            var pShot = cmd.Parameters.Add("@shot", SqliteType.Integer);
            var pSize = cmd.Parameters.Add("@size", SqliteType.Integer);
            var pCreated = cmd.Parameters.Add("@created", SqliteType.Text);
            var pModified = cmd.Parameters.Add("@modified", SqliteType.Text);
            var pFirst = cmd.Parameters.Add("@first", SqliteType.Text);
            var pLast = cmd.Parameters.Add("@last", SqliteType.Text);

            foreach (var record in records)
            {
                pPath.Value = record.FullPath;
                pName.Value = record.Name;
                pRoot.Value = record.RootPath;
                pRootLabel.Value = record.RootLabel;
                pExt.Value = record.Extension;
                pCategory.Value = record.Category;
                pShot.Value = record.IsScreenshot ? 1 : 0;
                pSize.Value = record.Size;
                pCreated.Value = DbTime.ToDb(record.Created);
                pModified.Value = DbTime.ToDb(record.Modified);
                pFirst.Value = scanStart;
                pLast.Value = scanStart;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>Marks rows that were not touched by the current scan as missing.</summary>
    public void MarkMissing(string rootPath, string scanStart)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
UPDATE files SET is_deleted = 1, deleted_at = @now
WHERE root = @root AND is_deleted = 0 AND last_seen < @now";
            cmd.Parameters.AddWithValue("@now", scanStart);
            cmd.Parameters.AddWithValue("@root", rootPath);
            cmd.ExecuteNonQuery();
        }
    }

    public List<ActivityEvent> GetNewSince(string scanStart)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT path, name, category, size, first_seen FROM files
                                WHERE first_seen = @s AND is_deleted = 0";
            cmd.Parameters.AddWithValue("@s", scanStart);
            return ReadEvents(cmd, "added");
        }
    }

    public List<ActivityEvent> GetRemovedSince(string scanStart)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT path, name, category, size, deleted_at FROM files
                                WHERE is_deleted = 1 AND deleted_at = @s";
            cmd.Parameters.AddWithValue("@s", scanStart);
            return ReadEvents(cmd, "deleted");
        }
    }

    private static List<ActivityEvent> ReadEvents(SqliteCommand cmd, string kind)
    {
        var list = new List<ActivityEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ActivityEvent
            {
                Kind = kind,
                FullPath = reader.GetString(0),
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                Size = reader.GetInt64(3),
                At = DbTime.FromDb(reader.GetString(4)),
            });
        }

        return list;
    }

    public void AddEvents(IEnumerable<ActivityEvent> events)
    {
        lock (_gate)
        {
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO events (kind, path, name, category, size, at)
                                VALUES (@k, @p, @n, @c, @s, @a)";
            var pk = cmd.Parameters.Add("@k", SqliteType.Text);
            var pp = cmd.Parameters.Add("@p", SqliteType.Text);
            var pn = cmd.Parameters.Add("@n", SqliteType.Text);
            var pc = cmd.Parameters.Add("@c", SqliteType.Text);
            var ps = cmd.Parameters.Add("@s", SqliteType.Integer);
            var pa = cmd.Parameters.Add("@a", SqliteType.Text);

            foreach (var e in events)
            {
                pk.Value = e.Kind;
                pp.Value = e.FullPath;
                pn.Value = e.Name;
                pc.Value = e.Category;
                ps.Value = e.Size;
                pa.Value = DbTime.ToDb(e.At);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            using var prune = _connection.CreateCommand();
            prune.CommandText = "DELETE FROM events WHERE at < @cutoff";
            prune.Parameters.AddWithValue("@cutoff", DbTime.ToDb(DateTime.Now.AddDays(-90)));
            prune.ExecuteNonQuery();
        }
    }

    public List<ActivityEvent> GetRecentEvents(int limit)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT kind, path, name, category, size, at FROM events
                                ORDER BY at DESC LIMIT @n";
            cmd.Parameters.AddWithValue("@n", limit);
            var list = new List<ActivityEvent>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ActivityEvent
                {
                    Id = 0,
                    Kind = reader.GetString(0),
                    FullPath = reader.GetString(1),
                    Name = reader.GetString(2),
                    Category = reader.GetString(3),
                    Size = reader.GetInt64(4),
                    At = DbTime.FromDb(reader.GetString(5)),
                });
            }

            return list;
        }
    }

    public List<FileEntry> QueryFiles(FileQuery query, out int total)
    {
        lock (_gate)
        {
            var where = new List<string> { "is_deleted = 0" };
            var args = new List<(string Name, object? Value)>();

            if (!query.IncludeProtected)
            {
                where.Add("is_protected = 0");
            }

            if (!string.IsNullOrWhiteSpace(query.RootPath))
            {
                where.Add("root = @root");
                args.Add(("@root", query.RootPath));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                where.Add(@"(name LIKE @search ESCAPE '\' OR path LIKE @search ESCAPE '\')");
                args.Add(("@search", "%" + EscapeLike(query.Search.Trim()) + "%"));
            }

            if (query.Categories is { Count: > 0 })
            {
                var placeholders = new List<string>();
                for (var i = 0; i < query.Categories.Count; i++)
                {
                    placeholders.Add($"@cat{i}");
                    args.Add(($"@cat{i}", query.Categories[i]));
                }

                where.Add($"category IN ({string.Join(", ", placeholders)})");
            }

            if (query.From is { } from)
            {
                where.Add("modified >= @from");
                args.Add(("@from", DbTime.ToDb(from)));
            }

            if (query.To is { } to)
            {
                where.Add("modified <= @to");
                args.Add(("@to", DbTime.ToDb(to)));
            }

            if (query.MinSize is { } minSize)
            {
                where.Add("size >= @min");
                args.Add(("@min", minSize));
            }

            if (query.MaxSize is { } maxSize)
            {
                where.Add("size <= @max");
                args.Add(("@max", maxSize));
            }

            if (query.ScreenshotsOnly)
            {
                where.Add("is_screenshot = 1");
            }

            if (query.InstallersOnly)
            {
                where.Add("category = 'Installer'");
            }

            var whereSql = " WHERE " + string.Join(" AND ", where);
            var orderSql = query.Sort switch
            {
                "name_asc" => " ORDER BY name COLLATE NOCASE ASC",
                "name_desc" => " ORDER BY name COLLATE NOCASE DESC",
                "size_asc" => " ORDER BY size ASC",
                "size_desc" => " ORDER BY size DESC",
                "modified_asc" => " ORDER BY modified ASC",
                "created_desc" => " ORDER BY created DESC",
                "first_desc" => " ORDER BY first_seen DESC",
                _ => " ORDER BY modified DESC",
            };

            total = (int)ScalarLong("SELECT COUNT(*) FROM files" + whereSql, args);

            var list = new List<FileEntry>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, name, root, root_label, ext, category, is_screenshot,
       size, created, modified, first_seen, last_seen, is_protected
FROM files" + whereSql + orderSql + " LIMIT @limit";
            AddParams(cmd, args);
            cmd.Parameters.AddWithValue("@limit", Math.Clamp(query.Limit, 1, 50000));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadFileEntry(reader));
            }

            return list;
        }
    }

    public List<FileEntry> QueryLargeFiles(long minBytes, int limit)
    {
        var query = new FileQuery
        {
            MinSize = minBytes,
            Sort = "size_desc",
            Limit = limit,
            IncludeProtected = false,
        };
        return QueryFiles(query, out _);
    }

    public List<FileEntry> QueryOldFiles(DateTime cutoff, string? rootPath, bool screenshotsOnly, int limit)
    {
        var query = new FileQuery
        {
            To = cutoff,
            RootPath = rootPath,
            ScreenshotsOnly = screenshotsOnly,
            Sort = "modified_asc",
            Limit = limit,
            IncludeProtected = false,
        };
        return QueryFiles(query, out _);
    }

    public List<FileEntry> QueryInstallers(int limit)
    {
        var query = new FileQuery
        {
            Categories = new[] { FileKinds.Installer },
            Sort = "modified_desc",
            Limit = limit,
        };
        return QueryFiles(query, out _);
    }

    /// <summary>Files whose size is shared with at least one other file (duplicate candidates).</summary>
    public List<FileEntry> QueryDuplicateCandidates(long minSize, int maxGroups)
    {
        lock (_gate)
        {
            var sizes = new List<long>();
            using (var sizeCmd = _connection.CreateCommand())
            {
                sizeCmd.CommandText = @"
SELECT size FROM files
WHERE is_deleted = 0 AND is_protected = 0 AND size >= @min
GROUP BY size HAVING COUNT(*) > 1
ORDER BY size DESC LIMIT @g";
                sizeCmd.Parameters.AddWithValue("@min", minSize);
                sizeCmd.Parameters.AddWithValue("@g", maxGroups);
                using var reader = sizeCmd.ExecuteReader();
                while (reader.Read())
                {
                    sizes.Add(reader.GetInt64(0));
                }
            }

            if (sizes.Count == 0)
            {
                return new List<FileEntry>();
            }

            var placeholders = string.Join(", ", sizes.Select((_, i) => $"@s{i}"));
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, name, root, root_label, ext, category, is_screenshot,
       size, created, modified, first_seen, last_seen, is_protected
FROM files
WHERE is_deleted = 0 AND is_protected = 0 AND size IN (" + placeholders + ")";
            for (var i = 0; i < sizes.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@s{i}", sizes[i]);
            }

            var list = new List<FileEntry>();
            using var fileReader = cmd.ExecuteReader();
            while (fileReader.Read())
            {
                list.Add(ReadFileEntry(fileReader));
            }

            return list;
        }
    }

    /// <summary>Largest indexed folders, used by the dashboard analytics card.</summary>
    public List<FolderStat> GetTopFolders(int limit)
    {
        var sizes = new Dictionary<string, (int Count, long Size)>(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT path, size FROM files WHERE is_deleted = 0";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                var size = reader.GetInt64(1);
                var folder = System.IO.Path.GetDirectoryName(path) ?? path;

                if (sizes.TryGetValue(folder, out var existing))
                {
                    sizes[folder] = (existing.Count + 1, existing.Size + size);
                }
                else
                {
                    sizes[folder] = (1, size);
                }
            }
        }

        var total = sizes.Values.Sum(value => value.Size);
        return sizes
            .OrderByDescending(pair => pair.Value.Size)
            .Take(limit)
            .Select(pair => new FolderStat
            {
                Folder = pair.Key,
                Count = pair.Value.Count,
                Size = pair.Value.Size,
                Percent = total > 0 ? pair.Value.Size * 100.0 / total : 0,
            })
            .ToList();
    }

    public List<FileEntry> QueryRecentFiles(int limit)
    {
        var query = new FileQuery { Sort = "first_desc", Limit = limit };
        return QueryFiles(query, out _);
    }

    /// <summary>Storage breakdown by file category for the dashboard.</summary>
    public List<CategoryStat> GetCategoryBreakdown()
    {
        lock (_gate)
        {
            var list = new List<CategoryStat>();
            var total = ScalarLong("SELECT COALESCE(SUM(size), 0) FROM files WHERE is_deleted = 0");
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT category, COUNT(*), COALESCE(SUM(size), 0)
FROM files WHERE is_deleted = 0
GROUP BY category
ORDER BY 3 DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var size = reader.GetInt64(2);
                list.Add(new CategoryStat
                {
                    Category = reader.GetString(0),
                    Count = (int)reader.GetInt64(1),
                    Size = size,
                    Percent = total > 0 ? size * 100.0 / total : 0,
                });
            }

            return list;
        }
    }

    private static FileEntry ReadFileEntry(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        FullPath = reader.GetString(1),
        Name = reader.GetString(2),
        Root = reader.GetString(3),
        RootLabel = reader.GetString(4),
        Extension = reader.GetString(5),
        Category = reader.GetString(6),
        IsScreenshot = reader.GetInt64(7) != 0,
        Size = reader.GetInt64(8),
        Created = DbTime.FromDb(reader.GetString(9)),
        Modified = DbTime.FromDb(reader.GetString(10)),
        FirstSeen = DbTime.FromDb(reader.GetString(11)),
        LastSeen = DbTime.FromDb(reader.GetString(12)),
        Keep = reader.GetInt64(13) != 0,
    };

    public IndexStats GetStats(int cleanupAgeDays)
    {
        lock (_gate)
        {
            var stats = new IndexStats
            {
                TotalFiles = (int)ScalarLong("SELECT COUNT(*) FROM files WHERE is_deleted = 0"),
                TotalSize = ScalarLong("SELECT COALESCE(SUM(size), 0) FROM files WHERE is_deleted = 0"),
                Screenshots = (int)ScalarLong("SELECT COUNT(*) FROM files WHERE is_deleted = 0 AND is_screenshot = 1"),
                ScreenshotsSize = ScalarLong("SELECT COALESCE(SUM(size), 0) FROM files WHERE is_deleted = 0 AND is_screenshot = 1"),
                Installers = (int)ScalarLong("SELECT COUNT(*) FROM files WHERE is_deleted = 0 AND category = 'Installer'"),
                InstallersSize = ScalarLong("SELECT COALESCE(SUM(size), 0) FROM files WHERE is_deleted = 0 AND category = 'Installer'"),
            };

            var todayStart = DbTime.ToDb(DateTime.Today);
            stats.NewToday = (int)ScalarLong("SELECT COUNT(*) FROM files WHERE first_seen >= @t AND is_deleted = 0",
                new[] { ("@t", (object?)todayStart) });
            stats.NewTodaySize = ScalarLong("SELECT COALESCE(SUM(size), 0) FROM files WHERE first_seen >= @t AND is_deleted = 0",
                new[] { ("@t", (object?)todayStart) });

            var cutoff = DbTime.ToDb(DateTime.Now.AddDays(-cleanupAgeDays));
            stats.OldDownloads = (int)ScalarLong(
                "SELECT COUNT(*) FROM files WHERE is_deleted = 0 AND modified < @c AND root_label = 'Downloads'",
                new[] { ("@c", (object?)cutoff) });
            stats.OldDownloadsSize = ScalarLong(
                "SELECT COALESCE(SUM(size), 0) FROM files WHERE is_deleted = 0 AND modified < @c AND root_label = 'Downloads'",
                new[] { ("@c", (object?)cutoff) });

            stats.LastScan = GetSetting("last_scan_utc") is { } last
                ? DbTime.FromDb(last)
                : null;
            return stats;
        }
    }

    // ------------------------------------------------------------- mutations

    public void SetProtected(long id, bool value)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE files SET is_protected = @v WHERE id = @id";
            cmd.Parameters.AddWithValue("@v", value ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteRow(long id)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateAfterRename(long id, string newPath, string newName, string newExtension, string newCategory, bool isScreenshot)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"UPDATE files
SET path = @p, name = @n, ext = @e, category = @c, is_screenshot = @s
WHERE id = @id";
            cmd.Parameters.AddWithValue("@p", newPath);
            cmd.Parameters.AddWithValue("@n", newName);
            cmd.Parameters.AddWithValue("@e", newExtension);
            cmd.Parameters.AddWithValue("@c", newCategory);
            cmd.Parameters.AddWithValue("@s", isScreenshot ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public long FindIdByPath(string path)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM files WHERE path = @p COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("@p", path);
            var result = cmd.ExecuteScalar();
            return result is null or DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
    }

    public void ClearIndex()
    {
        lock (_gate)
        {
            Execute("DELETE FROM files;");
            Execute("DELETE FROM events;");
        }
    }

    // -------------------------------------------------------------- settings

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = @k";
            cmd.Parameters.AddWithValue("@k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string? value)
    {
        lock (_gate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO settings (key, value) VALUES (@k, @v)
                                ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------------------------------------------------- disk history

    /// <summary>Stores one disk-space sample and prunes anything older than 30 days.</summary>
    public void AddDiskSample(long freeBytes, long totalBytes)
    {
        lock (_gate)
        {
            using var insert = _connection.CreateCommand();
            insert.CommandText = "INSERT INTO disk_history (at, free_bytes, total_bytes) VALUES (@a, @f, @t)";
            insert.Parameters.AddWithValue("@a", DbTime.NowDb());
            insert.Parameters.AddWithValue("@f", freeBytes);
            insert.Parameters.AddWithValue("@t", totalBytes);
            insert.ExecuteNonQuery();

            using var prune = _connection.CreateCommand();
            prune.CommandText = "DELETE FROM disk_history WHERE at < @cutoff";
            prune.Parameters.AddWithValue("@cutoff", DbTime.ToDb(DateTime.Now.AddDays(-30)));
            prune.ExecuteNonQuery();
        }
    }

    public List<DiskSample> GetDiskHistory(DateTime since)
    {
        lock (_gate)
        {
            var list = new List<DiskSample>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT at, free_bytes, total_bytes FROM disk_history
                                WHERE at >= @since ORDER BY at ASC";
            cmd.Parameters.AddWithValue("@since", DbTime.ToDb(since));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new DiskSample
                {
                    At = DbTime.FromDb(reader.GetString(0)),
                    FreeBytes = reader.GetInt64(1),
                    TotalBytes = reader.GetInt64(2),
                });
            }

            return list;
        }
    }

    // --------------------------------------------------------------- helpers

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long ScalarLong(string sql, IEnumerable<(string Name, object? Value)>? args = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (args is not null)
        {
            AddParams(cmd, args);
        }

        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static void AddParams(SqliteCommand cmd, IEnumerable<(string Name, object? Value)> args)
    {
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
             .Replace("%", @"\%", StringComparison.Ordinal)
             .Replace("_", @"\_", StringComparison.Ordinal);

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>Aggregate counters shown on the dashboard.</summary>
public sealed class IndexStats
{
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public int NewToday { get; set; }
    public long NewTodaySize { get; set; }
    public int Screenshots { get; set; }
    public long ScreenshotsSize { get; set; }
    public int Installers { get; set; }
    public long InstallersSize { get; set; }
    public int OldDownloads { get; set; }
    public long OldDownloadsSize { get; set; }
    public DateTime? LastScan { get; set; }

    public string TotalSizeText => Core.Formats.Bytes(TotalSize);
    public string NewTodaySizeText => Core.Formats.Bytes(NewTodaySize);
    public string OldDownloadsSizeText => Core.Formats.Bytes(OldDownloadsSize);
    public string ScreenshotsSizeText => Core.Formats.Bytes(ScreenshotsSize);
    public string LastScanText => LastScan is null ? "Never" : Core.Formats.Relative(LastScan.Value);
}
