namespace AlphaLedger.Core.Models;

public class DuplicateMatch
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
