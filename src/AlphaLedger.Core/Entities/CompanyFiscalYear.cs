using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Entities;

public class CompanyFiscalYear
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string YearLabel { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FiscalYearStatus Status { get; set; } = FiscalYearStatus.Active;
    public string? Notes { get; set; }
    public string? FolderPath { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Company? Company { get; set; }
}
