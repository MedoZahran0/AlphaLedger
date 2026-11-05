using AlphaLedger.App.Services;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class FeeEditViewModel : ObservableObject
{
    private readonly IFeeService _feeService;
    private readonly ICollectionService _collectionService;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid? _feeId;

    public FeeEditViewModel(IFeeService feeService, ICollectionService collectionService, WindowsDialogService dialogs, Guid? feeId = null)
    {
        _feeService = feeService;
        _collectionService = collectionService;
        _dialogs = dialogs;
        _feeId = feeId;

        Description = string.Empty;
        Amount = 0;
        Currency = "SAR";
        FeeStatus = FeeStatus.Agreed;
        DueDate = null;
        Notes = string.Empty;
    }

    /// <summary>Loads an existing fee without blocking the UI thread.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_feeId is not Guid id)
        {
            return;
        }

        var fee = await _feeService.GetFeeAsync(id, cancellationToken);
        if (fee is null)
        {
            return;
        }

        Description = fee.Description;
        Amount = fee.Amount;
        Currency = fee.Currency;
        FeeStatus = fee.Status;
        DueDate = fee.DueDate;
        Notes = fee.Notes ?? string.Empty;
    }

    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _currency = "SAR";
    [ObservableProperty] private FeeStatus _feeStatus = FeeStatus.Agreed;
    [ObservableProperty] private DateOnly? _dueDate;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private bool _isSaving = false;

    [RelayCommand]
    public async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var fee = new Fee
            {
                Id = _feeId ?? Guid.NewGuid(),
                CompanyId = Guid.Empty,
                Description = Description,
                Amount = Amount,
                Currency = Currency,
                Status = FeeStatus,
                DueDate = DueDate,
                Notes = Notes,
                FeeDate = DateOnly.FromDateTime(DateTime.Now),
                CreatedAtUtc = DateTime.UtcNow
            };

            if (_feeId.HasValue)
                await _feeService.UpdateFeeAsync(fee);
            else
                await _feeService.AddFeeAsync(fee);

            IsSaving = false;
            _dialogs.Info(LocalizationService.Instance.GetString(_feeId.HasValue ? "FeeUpdated" : "FeeAdded"));
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
}