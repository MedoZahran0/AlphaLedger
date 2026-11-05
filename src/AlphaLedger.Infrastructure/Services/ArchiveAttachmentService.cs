using System.Data;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class ArchiveAttachmentService : IArchiveAttachmentService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly ICompanyArchiveService _archive;
    private readonly IClock _clock;

    public ArchiveAttachmentService(AlphaLedgerDbContext db, ICompanyArchiveService archive, IClock clock)
    {
        _db = db;
        _archive = archive;
        _clock = clock;
    }

    public async Task AttachAsync(Guid companyId, string companyCode, string entityType, Guid entityId, int categoryNumber, string sourcePath, CancellationToken cancellationToken = default)
    {
        var fiscalYearId = await ResolveFiscalYearAsync(entityType, entityId, cancellationToken);
        var path = await _archive.ImportFileToCategoryAsync(companyId, companyCode, categoryNumber, sourcePath, cancellationToken, fiscalYearId);
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO ArchiveAttachments (Id, CompanyId, EntityType, EntityId, CategoryNumber, FilePath, FileName, AttachedAtUtc) VALUES (@id,@cid,@et,@eid,@cat,@path,@name,@at);";
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@id", Guid.NewGuid().ToString()));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@cid", companyId.ToString()));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@et", entityType));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@eid", entityId.ToString()));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@cat", categoryNumber));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@path", path));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@name", Path.GetFileName(path)));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@at", _clock.UtcNow.ToString("O")));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Guid?> ResolveFiscalYearAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        switch (entityType)
        {
            case "Fee":
                return await _db.Fees.AsNoTracking().Where(item => item.Id == entityId).Select(item => item.FiscalYearId).FirstOrDefaultAsync(cancellationToken);
            case "TaxReturn":
                return await _db.TaxReturns.AsNoTracking().Where(item => item.Id == entityId).Select(item => item.FiscalYearId).FirstOrDefaultAsync(cancellationToken);
            case "Operation":
                return await _db.Operations.AsNoTracking().Where(item => item.Id == entityId).Select(item => item.FiscalYearId).FirstOrDefaultAsync(cancellationToken);
            case "Collection":
                var collection = await _db.Collections.AsNoTracking()
                    .Where(item => item.Id == entityId)
                    .Select(item => new { item.FeeId, item.OperationId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (collection?.FeeId is Guid feeId)
                    return await _db.Fees.AsNoTracking().Where(item => item.Id == feeId).Select(item => item.FiscalYearId).FirstOrDefaultAsync(cancellationToken);
                if (collection?.OperationId is Guid operationId)
                    return await _db.Operations.AsNoTracking().Where(item => item.Id == operationId).Select(item => item.FiscalYearId).FirstOrDefaultAsync(cancellationToken);
                return null;
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<ArchiveAttachment>> GetForEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var result = new List<ArchiveAttachment>();
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, CompanyId, EntityType, EntityId, CategoryNumber, FilePath, FileName, AttachedAtUtc FROM ArchiveAttachments WHERE EntityType=@et AND EntityId=@eid;";
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@et", entityType));
        cmd.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@eid", entityId.ToString()));
        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            result.Add(new ArchiveAttachment
            {
                Id = Guid.Parse(r.GetString(0)),
                CompanyId = Guid.Parse(r.GetString(1)),
                EntityType = r.GetString(2),
                EntityId = Guid.Parse(r.GetString(3)),
                CategoryNumber = r.GetInt32(4),
                FilePath = r.GetString(5),
                FileName = r.GetString(6),
                AttachedAtUtc = DateTime.Parse(r.GetString(7))
            });
        }
        return result;
    }

    public async Task UpdatePathsAsync(string oldPath, string newPath, bool includeDescendants = false, CancellationToken cancellationToken = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(cancellationToken);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDescendants
            ? "UPDATE ArchiveAttachments SET FilePath=@new || substr(FilePath, length(@old) + 1) WHERE FilePath=@old OR substr(FilePath, 1, length(@old) + 1)=@old || '/' OR substr(FilePath, 1, length(@old) + 1)=@old || char(92);"
            : "UPDATE ArchiveAttachments SET FilePath=@new, FileName=@name WHERE FilePath=@old;";
        cmd.Parameters.Add(new SqliteParameter("@old", oldPath));
        cmd.Parameters.Add(new SqliteParameter("@new", newPath));
        if (!includeDescendants) cmd.Parameters.Add(new SqliteParameter("@name", Path.GetFileName(newPath)));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
