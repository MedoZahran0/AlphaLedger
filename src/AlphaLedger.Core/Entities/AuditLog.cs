namespace AlphaLedger.Core.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Actor { get; set; }
}
