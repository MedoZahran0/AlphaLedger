using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Entities;

public class TaxReturn
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FiscalYearId { get; set; }
    public string TaxType { get; set; } = string.Empty;
    public string TaxPeriod { get; set; } = string.Empty;
    public DateOnly FilingDueDate { get; set; }
    public string PreparationStatus { get; set; } = "لم يبدأ";
    public string ReviewStatus { get; set; } = "غير مراجع";
    public TaxReturnFilingStatus FilingStatus { get; set; } = TaxReturnFilingStatus.NotStarted;
    public DateOnly? SubmissionDate { get; set; }
    public string? OfficialReceiptNumber { get; set; }
    public Guid? ResponsibleEmployeeId { get; set; }
    public Guid? RelatedOperationId { get; set; }
    public Guid? RelatedFeeId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Company? Company { get; set; }
}
