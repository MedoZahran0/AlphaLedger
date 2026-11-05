using AlphaLedger.App.Services;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class CollectionEditViewModel : ObservableObject
{
    private readonly ICollectionService _collectionService;
    private readonly IFeeService _feeService;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid? _collectionId;
    private readonly Guid? _feeId;

    public CollectionEditViewModel(ICollectionService collectionService, IFeeService feeService, WindowsDialogService dialogs, Guid? collectionId = null, Guid? feeId = null)
    {
        _collectionService = collectionService;
        _feeService = feeService;
        _dialogs = dialogs;
        _collectionId = collectionId;
        _feeId = feeId;

        PaymentMethod = LocalizationService.Instance.GetString("PaymentMethodCash");
        CollectionDate = DateOnly.FromDateTime(DateTime.Now);
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    [ObservableProperty] private string _paymentMethod = string.Empty;
    [ObservableProperty] private DateOnly _collectionDate;
    [ObservableProperty] private decimal _amountReceived;
    [ObservableProperty] private string _receiptNumber = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private bool _isSaving = false;

    [RelayCommand]
    public async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var collection = new Collection
            {
                Id = _collectionId ?? Guid.NewGuid(),
                CompanyId = Guid.NewGuid(), // Will be set by the caller
                FeeId = _feeId,
                CollectionDate = CollectionDate,
                AmountReceived = AmountReceived,
                PaymentMethod = PaymentMethod,
                ReceiptNumber = ReceiptNumber,
                Notes = Notes,
                IsReversed = false
            };

            await _collectionService.AddCollectionAsync(collection);
            IsSaving = false;
            _dialogs.Info(LocalizationService.Instance.GetString("CollectionRecorded"));
        }
        catch (Exception ex)
        {
            IsSaving = false;
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        // Close dialog
    }

    private void OnCultureChanged()
    {
        if (PaymentMethod == "Cash" || PaymentMethod == "نقدي")
            PaymentMethod = LocalizationService.Instance.GetString("PaymentMethodCash");
    }
}