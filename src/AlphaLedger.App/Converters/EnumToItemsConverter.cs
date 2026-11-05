using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Enums;

namespace AlphaLedger.App.Converters;

public class EnumToItemsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            var type = enumValue.GetType();
            var items = new ObservableCollection<object>();
            foreach (var val in Enum.GetValues(type))
            {
                items.Add(val);
            }
            return items;
        }
        return new ObservableCollection<object>();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EnumToDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            var key = enumValue switch
            {
                FiscalYearStatus.Planned => "FiscalYearsStatusPlanned",
                FiscalYearStatus.Active => "FiscalYearsStatusActive",
                FiscalYearStatus.Closed => "FiscalYearsStatusClosed",
                FeeStatus.Draft => "FeeStatusDraft",
                FeeStatus.Agreed => "FeeStatusAgreed",
                FeeStatus.PartiallyCollected => "FeeStatusPartiallyCollected",
                FeeStatus.Collected => "FeeStatusCollected",
                FeeStatus.Cancelled => "FeeStatusCancelled",
                OperationStatus.NotStarted => "OperationsStatusNotStarted",
                OperationStatus.InProgress => "OperationsStatusInProgress",
                OperationStatus.WaitingForDocuments => "OperationsStatusWaitingDocuments",
                OperationStatus.UnderReview => "OperationsStatusUnderReview",
                OperationStatus.ReadyForDelivery => "OperationsStatusReadyForDelivery",
                OperationStatus.SubmittedOrDelivered => "OperationsStatusSubmitted",
                OperationStatus.Completed => "OperationsStatusCompleted",
                OperationStatus.Cancelled => "OperationsStatusCancelled",
                OperationPriority.Low => "OperationsPriorityLow",
                OperationPriority.Medium => "OperationsPriorityMedium",
                OperationPriority.High => "OperationsPriorityHigh",
                OperationPriority.Urgent => "OperationsPriorityUrgent",
                TaxReturnFilingStatus.NotStarted => "TaxReturnsStatusNotStarted",
                TaxReturnFilingStatus.InPreparation => "TaxReturnsStatusInPreparation",
                TaxReturnFilingStatus.AwaitingReview => "TaxReturnsStatusAwaitingReview",
                TaxReturnFilingStatus.ReadyToFile => "TaxReturnsStatusReadyToFile",
                TaxReturnFilingStatus.Submitted => "TaxReturnsStatusSubmitted",
                TaxReturnFilingStatus.Overdue => "TaxReturnsStatusOverdue",
                _ => null
            };

            return key is null ? enumValue.ToString() : LocalizationService.Instance.GetString(key);
        }
        return value ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class TaxTypeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "VAT" or "Value Added Tax (VAT)" or "ضريبة القيمة المضافة" => LocalizationService.Instance.GetString("TaxReturnsTypeVAT"),
            "IncomeTax" or "Income Tax" or "ضريبة الدخل" => LocalizationService.Instance.GetString("TaxReturnsTypeIncomeTax"),
            "WithholdingTax" or "Withholding and Collection Tax" or "الضريبة المقتطعة والمضافة" => LocalizationService.Instance.GetString("TaxReturnsTypeWithholding"),
            "PayrollTax" or "Payroll and Employment Tax" or "ضريبة المرتبات وكسب العمل" => LocalizationService.Instance.GetString("TaxReturnsTypePayroll"),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}