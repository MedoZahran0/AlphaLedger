using AlphaLedger.Core.Entities;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Enums;

namespace AlphaLedger.App.ViewModels;

public class FeeListItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? FiscalYearId { get; set; }
    public string? FiscalYearLabel { get; set; }
    public Guid? OperationId { get; set; }
    public string? OperationDescription { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public DateOnly FeeDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public DateOnly? DueDate { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding { get; set; }
    public FeeStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CollectionListItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FeeId { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public decimal AmountReceived { get; set; }
    public string PaymentMethod { get; set; } = LocalizationService.Instance.GetString("PaymentMethodCash");
    public DateOnly CollectionDate { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public bool IsReversed { get; set; }
    public string? ReversalReason { get; set; }
}