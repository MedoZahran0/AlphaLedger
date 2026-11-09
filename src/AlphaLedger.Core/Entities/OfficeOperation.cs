using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Entities;

public class OfficeOperation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FiscalYearId { get; set; }
    public string ServiceCategory { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TaxPeriod { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.NotStarted;
    public OperationPriority Priority { get; set; } = OperationPriority.Medium;
    public decimal AgreedFee { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }

    public Company? Company { get; set; }
    public CompanyFiscalYear? FiscalYear { get; set; }
    public Employee? AssignedEmployee { get; set; }
}
