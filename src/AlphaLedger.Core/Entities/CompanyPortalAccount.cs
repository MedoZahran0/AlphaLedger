namespace AlphaLedger.Core.Entities;

public class CompanyPortalAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string PortalName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public byte[]? ProtectedPassword { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Company? Company { get; set; }
}