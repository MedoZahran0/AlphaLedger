using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Entities;

public class Fee
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FiscalYearId { get; set; }
    public Guid? OperationId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateOnly FeeDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? PaymentTerms { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Notes { get; set; }
    public FeeStatus Status { get; set; } = FeeStatus.Agreed;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Company? Company { get; set; }
    public CompanyFiscalYear? FiscalYear { get; set; }
    public OfficeOperation? Operation { get; set; }
    public ICollection<Collection> Collections { get; set; } = new List<Collection>();
}
