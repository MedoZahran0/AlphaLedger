namespace AlphaLedger.Core.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FeeId { get; set; }
    public Guid? OperationId { get; set; }
    public DateOnly CollectionDate { get; set; }
    public decimal AmountReceived { get; set; }
    public string PaymentMethod { get; set; } = "نقدي";
    public string? ReceiptNumber { get; set; }
    public Guid? ReceivedByEmployeeId { get; set; }
    public string? Notes { get; set; }
    public bool IsReversed { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Company? Company { get; set; }
    public Fee? Fee { get; set; }
}
