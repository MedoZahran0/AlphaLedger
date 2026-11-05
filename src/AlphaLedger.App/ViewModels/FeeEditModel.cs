using AlphaLedger.Core.Enums;
using AlphaLedger.App.Localization;
using System;
using System.ComponentModel.DataAnnotations;

namespace AlphaLedger.App.ViewModels;

public class FeeEditModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? OperationId { get; set; }
    [Required(ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationDescriptionRequired))]
    public string Description { get; set; } = string.Empty;
    public DateOnly FeeDate { get; set; }
    [Required(ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationAmountRequired))]
    public decimal Amount { get; set; }
    [Required(ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationCurrencyRequired))]
    [StringLength(8, ErrorMessageResourceType = typeof(LocalizationService), ErrorMessageResourceName = nameof(LocalizationService.ValidationCurrencyLength))]
    public string Currency { get; set; } = "SAR";
    public string? PaymentTerms { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Notes { get; set; }
    public FeeStatus Status { get; set; } = FeeStatus.Agreed;
}