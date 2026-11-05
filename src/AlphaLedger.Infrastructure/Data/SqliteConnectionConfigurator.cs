using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Data;

public static class SqliteConnectionConfigurator
{
    public static async Task ApplyPragmasAsync(AlphaLedgerDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);
    }

    public static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        return builder.ToString();
    }
}
