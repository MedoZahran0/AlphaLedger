namespace AlphaLedger.Core.Abstractions;

public interface IAuditService
{
    Task WriteAsync(string action, string entityType, string? entityId, string summary, string? actor = null, CancellationToken cancellationToken = default);
}
