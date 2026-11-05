using System;
using System.ComponentModel.DataAnnotations;
using AlphaLedger.App.Localization;

namespace AlphaLedger.App.ViewModels;

public class CollectionEditModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? FeeId { get; set; }
    public Guid? OperationId { get; set; }
    public DateOnly CollectionDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    [Required(ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationAmountRequired))]
    public decimal AmountReceived { get; set; }
    [Required(ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationPaymentMethodRequired))]
    [StringLength(50, ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationPaymentMethodLength))]
    public string PaymentMethod { get; set; } = LocalizationService.Instance.GetString("PaymentMethodCash");
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public bool IsReversed { get; set; }
}