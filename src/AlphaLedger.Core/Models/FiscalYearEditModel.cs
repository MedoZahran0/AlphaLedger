using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Models;

public class FiscalYearEditModel
{
    public string YearLabel { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public FiscalYearStatus Status { get; set; } = FiscalYearStatus.Active;
    public string? Notes { get; set; }
}
