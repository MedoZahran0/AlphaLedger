using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Infrastructure.Data;

namespace AlphaLedger.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IClock _clock;

    public AuditService(AlphaLedgerDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task WriteAsync(string action, string entityType, string? entityId, string summary, string? actor = null, CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = _clock.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Actor = actor ?? "مستخدم محلي"
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
