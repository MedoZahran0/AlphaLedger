using System.IO;
using System.Text.Json;
using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.App.Services;

public sealed class AppDataRelocationService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAppPaths _paths;
    private readonly ISettingsService _settings;

    public AppDataRelocationService(AlphaLedgerDbContext db, IAppPaths paths, ISettingsService settings)
    {
        _db = db;
        _paths = paths;
        _settings = settings;
    }

    public async Task<string> RelocateAsync(string selectedRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(selectedRoot.Trim());
        Directory.CreateDirectory(root);
        var archiveRoot = Path.Combine(root, "Archive");
        var systemRoot = Path.Combine(root, "System");
        var databasePath = Path.Combine(systemRoot, "Data", "alpha-ledger.db");

        if (File.Exists(databasePath) && !string.Equals(Path.GetFullPath(_paths.DatabasePath), Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase))
        {
            await PointToExistingDatabaseAsync(databasePath, archiveRoot, root, cancellationToken);
            return databasePath;
        }

        var oldArchiveRoot = await _settings.GetDocumentRootAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(oldArchiveRoot))
        {
            var oldArchive = Path.GetFullPath(oldArchiveRoot);
            if (!string.Equals(root, Path.GetDirectoryName(oldArchive), StringComparison.OrdinalIgnoreCase) &&
                (IsInside(root, oldArchive) || IsInside(oldArchive, root)))
                throw new InvalidOperationException("اختر مجلداً خارج مجلد الأرشيف الحالي حتى لا تتكرر الملفات داخل بعضها.");
            CopyDirectoryMerge(oldArchive, archiveRoot);
        }

        CopyDirectoryMerge(_paths.BackupsDirectory, Path.Combine(systemRoot, "Backups"));
        CopyDirectoryMerge(_paths.ConfigDirectory, Path.Combine(systemRoot, "Config"));
        CopyDirectoryMerge(_paths.LogsDirectory, Path.Combine(systemRoot, "Logs"));
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        await _settings.SetDocumentRootAsync(archiveRoot, cancellationToken);
        await UpdateStoredPathsAsync(oldArchiveRoot, archiveRoot, cancellationToken);

        if (!string.Equals(Path.GetFullPath(_paths.DatabasePath), Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase))
        {
        var sourceConnection = _db.Database.GetDbConnection();
        var shouldCloseSource = sourceConnection.State != System.Data.ConnectionState.Open;
        if (shouldCloseSource)
            await sourceConnection.OpenAsync(cancellationToken);
        try
        {
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ToString());
            await destination.OpenAsync(cancellationToken);
            if (sourceConnection is not SqliteConnection source)
                throw new InvalidOperationException("نوع قاعدة البيانات الحالي غير مدعوم للنقل.");
            source.BackupDatabase(destination);
        }
        finally
        {
            if (shouldCloseSource)
                await sourceConnection.CloseAsync();
        }
        }

        await SaveRootPointerAsync(root, archiveRoot, databasePath, cancellationToken);
        return databasePath;
    }

    private async Task PointToExistingDatabaseAsync(string databasePath, string archiveRoot, string root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync(cancellationToken);
        string? oldArchive = null;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT \"Value\" FROM \"AppSettings\" WHERE \"Key\"=@key LIMIT 1;";
            read.Parameters.AddWithValue("@key", SettingKeys.DocumentRootPath);
            oldArchive = await read.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (!string.IsNullOrWhiteSpace(oldArchive) && Directory.Exists(oldArchive) &&
            !string.Equals(Path.GetFullPath(oldArchive), Path.GetFullPath(archiveRoot), StringComparison.OrdinalIgnoreCase))
            CopyDirectoryMerge(oldArchive, archiveRoot);
        else
            Directory.CreateDirectory(archiveRoot);
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "INSERT INTO \"AppSettings\" (\"Key\", \"Value\", \"UpdatedAtUtc\") VALUES (@key, @archive, @updated) ON CONFLICT(\"Key\") DO UPDATE SET \"Value\"=excluded.\"Value\", \"UpdatedAtUtc\"=excluded.\"UpdatedAtUtc\";";
            update.Parameters.AddWithValue("@archive", archiveRoot);
            update.Parameters.AddWithValue("@key", SettingKeys.DocumentRootPath);
            update.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpdateStoredPathsAsync(connection, oldArchive, archiveRoot, cancellationToken);
        await SaveRootPointerAsync(root, archiveRoot, databasePath, cancellationToken);
    }

    private static async Task SaveRootPointerAsync(string root, string archiveRoot, string databasePath, CancellationToken cancellationToken)
    {
        var manifest = new
        {
            Application = "AlphaLedger",
            DataRoot = root,
            Database = Path.GetRelativePath(root, databasePath),
            Archive = Path.GetRelativePath(root, archiveRoot),
            UpdatedAtUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(Path.Combine(root, "AlphaLedger-data.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger");
        Directory.CreateDirectory(localRoot);
        await File.WriteAllTextAsync(Path.Combine(localRoot, "data-root.txt"), root, cancellationToken);
    }

    private async Task UpdateStoredPathsAsync(string? oldRoot, string newRoot, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(oldRoot))
            {
                await UpdateStoredPathsAsync(connection, oldRoot, newRoot, cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task UpdateStoredPathsAsync(System.Data.Common.DbConnection connection, string? oldRoot, string newRoot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(oldRoot))
        {
            foreach (var (table, column) in new[]
                         {
                             ("Companies", "DocumentStoragePath"),
                             ("CompanyFiscalYears", "FolderPath"),
                             ("ArchiveAttachments", "FilePath")
                         })
            {
                await using (var exists = connection.CreateCommand())
                {
                    exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table;";
                    exists.Parameters.Add(new SqliteParameter("@table", table));
                    if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
                        continue;
                }
                await using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = @new || substr(\"{column}\", length(@old) + 1) WHERE \"{column}\" = @old OR substr(\"{column}\", 1, length(@old) + 1) = @old || @separator;";
                command.Parameters.Add(new SqliteParameter("@old", oldRoot));
                command.Parameters.Add(new SqliteParameter("@new", newRoot));
                command.Parameters.Add(new SqliteParameter("@separator", Path.DirectorySeparatorChar.ToString()));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static void CopyDirectoryMerge(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }

    private static bool IsInside(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

}
