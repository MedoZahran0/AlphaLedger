using System.Collections.ObjectModel;
using System.Globalization;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class FeesViewModel : ObservableObject
{
    private readonly IFeeService _feeService;
    private readonly ICollectionService _collectionService;
    private readonly ICompanyService _companyService;
    private readonly IArchiveAttachmentService _attachmentService;
    private readonly ICompanyArchiveService _archiveService;
    private readonly WindowsDialogService _dialogs;
    private Guid? _editingFeeId;
    private Guid? _editingCollectionId;
    private bool _companyReloadPending;
    private readonly Guid? _initialCompanyId;

    public FeesViewModel(IFeeService feeService, ICollectionService collectionService, ICompanyService companyService, IArchiveAttachmentService attachmentService, ICompanyArchiveService archiveService, WindowsDialogService dialogs, Guid companyId = default, Guid fiscalYearId = default)
    {
        _feeService = feeService;
        _collectionService = collectionService;
        _companyService = companyService;
        _attachmentService = attachmentService;
        _archiveService = archiveService;
        _dialogs = dialogs;
        _initialCompanyId = companyId == Guid.Empty ? null : companyId;

        // Assign the backing fields directly so constructing the view model does not
        // trigger the company/fiscal-year change handlers and start a premature load.
        _selectedCompanyId = _initialCompanyId;
        _selectedFiscalYearId = fiscalYearId == Guid.Empty ? null : fiscalYearId;

        Items = new ObservableCollection<FeeListItem>();
        Collections = new ObservableCollection<CollectionListItem>();
        Companies = new ObservableCollection<CompanyListItem>();

        FilterStatuses = new ObservableCollection<FeeStatusOption>
        {
            new(null, Text("CommonAll")),
            new(FeeStatus.Draft, Text("FeeStatusDraft")),
            new(FeeStatus.Agreed, Text("FeeStatusAgreed")),
            new(FeeStatus.PartiallyCollected, Text("FeeStatusPartiallyCollected")),
            new(FeeStatus.Collected, Text("FeeStatusCollected")),
            new(FeeStatus.Cancelled, Text("FeeStatusCancelled"))
        };
        FeeStatuses = new ObservableCollection<FeeStatus>(Enum.GetValues<FeeStatus>());
        SelectedStatusOption = FilterStatuses[0];
        FeeDate = DateOnly.FromDateTime(DateTime.Today);
        CollectionDate = DateOnly.FromDateTime(DateTime.Today);
        Currency = "EGP";
        PaymentMethod = Text("PaymentMethodCash");
        LocalizationService.Instance.CultureChanged += RefreshFilterLabels;
    }

    public ObservableCollection<FeeListItem> Items { get; }
    public ObservableCollection<CollectionListItem> Collections { get; }

    [ObservableProperty] private FeeStatusOption? _selectedStatusOption;
    [ObservableProperty] private Guid? _selectedCompanyId;
    [ObservableProperty] private Guid? _selectedFiscalYearId;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showOnlyUncollected;
    [ObservableProperty] private DateOnly? _filterFromDate;
    [ObservableProperty] private DateOnly? _filterToDate;

    [ObservableProperty] private FeeListItem? _selectedFee;
    [ObservableProperty] private CollectionListItem? _selectedCollection;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private decimal _totalAgreedFees;
    [ObservableProperty] private decimal _totalCollected;
    [ObservableProperty] private decimal _outstandingBalance;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string _currency = "EGP";
    [ObservableProperty] private DateOnly _feeDate;
    [ObservableProperty] private DateOnly? _dueDate;
    [ObservableProperty] private string _paymentTerms = string.Empty;
    [ObservableProperty] private FeeStatus _feeStatus = FeeStatus.Agreed;
    [ObservableProperty] private string _feeNotes = string.Empty;
    [ObservableProperty] private decimal _collectionAmount;
    [ObservableProperty] private DateOnly _collectionDate;
    [ObservableProperty] private string _paymentMethod = string.Empty;
    [ObservableProperty] private string _receiptNumber = string.Empty;
    [ObservableProperty] private string _collectionNotes = string.Empty;
    [ObservableProperty] private string _reversalReason = string.Empty;

    public ObservableCollection<FeeStatusOption> FilterStatuses { get; }
    public ObservableCollection<FeeStatus> FeeStatuses { get; }
    public ObservableCollection<CompanyListItem> Companies { get; } = [];
    public ObservableCollection<CompanyFiscalYear> FiscalYears { get; } = [];
    public event Action<Guid>? OpenArchiveRequested;
    public event Action? DataChanged;

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            // A reload was requested while another one is running; run it once afterwards.
            _companyReloadPending = true;
            return;
        }

        IsLoading = true;
        try
        {
            var preferredCompanyId = SelectedCompanyId ?? _initialCompanyId;
            var companies = await _companyService.GetCompaniesAsync(new CompanyFilter());
            Companies.Clear();
            foreach (var company in companies)
                Companies.Add(company);

            if (preferredCompanyId is Guid preferred && Companies.Any(company => company.Id == preferred))
                SelectedCompanyId = preferred;
            else if (SelectedCompanyId is null && Companies.Count > 0)
                SelectedCompanyId = Companies[0].Id;

            await LoadFiscalYearsAsync();

            var companyId = SelectedCompanyId;
            var previousFeeId = SelectedFee?.Id;
            var fees = await _feeService.GetFeesAsync(companyId);
            var collections = await _collectionService.GetCollectionsAsync(companyId);
            var collectionTotals = collections.Where(item => !item.IsReversed && item.FeeId.HasValue)
                .GroupBy(item => item.FeeId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.AmountReceived));
            var fiscalYearNames = FiscalYears.ToDictionary(year => year.Id, year => year.YearLabel);

            Items.Clear();
            foreach (var fee in fees.Where(MatchesFilters))
            {
                var collected = collectionTotals.GetValueOrDefault(fee.Id);
                if (ShowOnlyUncollected && fee.Amount <= collected)
                    continue;

                Items.Add(new FeeListItem
                {
                    Id = fee.Id,
                    CompanyId = fee.CompanyId,
                    CompanyName = Companies.FirstOrDefault(company => company.Id == fee.CompanyId)?.DisplayName ?? string.Empty,
                    Description = fee.Description,
                    Amount = fee.Amount,
                    Currency = fee.Currency,
                    FiscalYearId = fee.FiscalYearId,
                    FiscalYearLabel = fee.FiscalYearId is Guid fiscalYearId && fiscalYearNames.TryGetValue(fiscalYearId, out var label) ? label : null,
                    FeeDate = fee.FeeDate,
                    DueDate = fee.DueDate,
                    Status = fee.Status,
                    Collected = collected,
                    Outstanding = fee.Status == FeeStatus.Cancelled ? 0 : fee.Amount - collected,
                    CreatedAt = fee.CreatedAtUtc
                });
            }

            TotalAgreedFees = await _feeService.GetTotalAgreedFeesAsync(companyId);
            TotalCollected = await _feeService.GetTotalCollectedAsync(companyId);
            OutstandingBalance = TotalAgreedFees - TotalCollected;
            SelectedFee = previousFeeId.HasValue
                ? Items.FirstOrDefault(item => item.Id == previousFeeId.Value)
                : Items.FirstOrDefault();
            if (StatusMessage.Length == 0)
                StatusMessage = Text("CommonReadyStatus");
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
        finally
        {
            IsLoading = false;
            if (_companyReloadPending)
            {
                _companyReloadPending = false;
                _ = LoadAsync();
            }
        }
    }

    [RelayCommand]
    public void NewFee()
    {
        _editingFeeId = null;
        Description = string.Empty;
        Amount = 0;
        Currency = "EGP";
        FeeDate = DateOnly.FromDateTime(DateTime.Today);
        DueDate = null;
        PaymentTerms = string.Empty;
        FeeStatus = FeeStatus.Agreed;
        FeeNotes = string.Empty;
    }

    [RelayCommand]
    public async Task EditFeeAsync()
    {
        if (SelectedFee is null)
        {
            _dialogs.Warning(Text("FeesSelectFee"));
            return;
        }

        var fee = await _feeService.GetFeeAsync(SelectedFee.Id);
        if (fee is null)
            return;

        _editingFeeId = fee.Id;
        SelectedCompanyId = fee.CompanyId;
        SelectedFiscalYearId = fee.FiscalYearId;
        Description = fee.Description;
        Amount = fee.Amount;
        Currency = fee.Currency;
        FeeDate = fee.FeeDate;
        DueDate = fee.DueDate;
        PaymentTerms = fee.PaymentTerms ?? string.Empty;
        FeeStatus = fee.Status;
        FeeNotes = fee.Notes ?? string.Empty;
    }

    [RelayCommand]
    public async Task SaveFeeAsync()
    {
        if (SelectedCompanyId is not Guid companyId)
        {
            _dialogs.Warning(Text("FeesSelectCompany"));
            return;
        }

        try
        {
            var fee = new Fee
            {
                Id = _editingFeeId ?? Guid.NewGuid(),
                CompanyId = companyId,
                FiscalYearId = SelectedFiscalYearId,
                Description = Description.Trim(),
                Amount = Amount,
                Currency = Currency.Trim(),
                FeeDate = FeeDate,
                DueDate = DueDate,
                PaymentTerms = string.IsNullOrWhiteSpace(PaymentTerms) ? null : PaymentTerms.Trim(),
                Status = FeeStatus,
                Notes = string.IsNullOrWhiteSpace(FeeNotes) ? null : FeeNotes.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            if (_editingFeeId.HasValue)
                await _feeService.UpdateFeeAsync(fee);
            else
                await _feeService.AddFeeAsync(fee);

            StatusMessage = Text(_editingFeeId.HasValue ? "FeeUpdated" : "FeeAdded");
            NewFee();
            await LoadAsync();
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public async Task CancelSelectedFeeAsync()
    {
        if (SelectedFee is null)
        {
            _dialogs.Warning(Text("FeesSelectFee"));
            return;
        }
        if (!_dialogs.Confirm(Text("FeesCancelConfirm")))
            return;

        var fee = await _feeService.GetFeeAsync(SelectedFee.Id);
        if (fee is null)
            return;
        fee.Status = FeeStatus.Cancelled;
        try
        {
            await _feeService.UpdateFeeAsync(fee);
            StatusMessage = Text("FeeCancelled");
            await LoadAsync();
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public async Task SaveCollectionAsync()
    {
        if (SelectedFee is null)
        {
            _dialogs.Warning(Text("FeesSelectFee"));
            return;
        }

        try
        {
            var collection = new Collection
            {
                Id = _editingCollectionId ?? Guid.NewGuid(),
                CompanyId = SelectedFee.CompanyId,
                FeeId = SelectedFee.Id,
                CollectionDate = CollectionDate,
                AmountReceived = CollectionAmount,
                PaymentMethod = PaymentMethod.Trim(),
                ReceiptNumber = string.IsNullOrWhiteSpace(ReceiptNumber) ? null : ReceiptNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(CollectionNotes) ? null : CollectionNotes.Trim()
            };

            if (_editingCollectionId.HasValue)
                await _collectionService.UpdateCollectionAsync(collection);
            else
                await _collectionService.AddCollectionAsync(collection);

            _editingCollectionId = null;
            CollectionAmount = 0;
            ReceiptNumber = string.Empty;
            CollectionNotes = string.Empty;
            StatusMessage = Text("CollectionRecorded");
            await LoadAsync();
            await LoadCollectionsForSelectedFeeAsync();
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public void EditSelectedCollection()
    {
        if (SelectedCollection is null || SelectedCollection.IsReversed)
        {
            _dialogs.Warning(Text("CollectionsSelectActive"));
            return;
        }

        _editingCollectionId = SelectedCollection.Id;
        CollectionAmount = SelectedCollection.AmountReceived;
        CollectionDate = SelectedCollection.CollectionDate;
        PaymentMethod = SelectedCollection.PaymentMethod;
        ReceiptNumber = SelectedCollection.ReceiptNumber ?? string.Empty;
        CollectionNotes = SelectedCollection.Notes ?? string.Empty;
    }

    [RelayCommand]
    public async Task ReverseSelectedCollectionAsync()
    {
        if (SelectedCollection is null || SelectedCollection.IsReversed)
        {
            _dialogs.Warning(Text("CollectionsSelectActive"));
            return;
        }
        if (string.IsNullOrWhiteSpace(ReversalReason))
        {
            _dialogs.Warning(Text("CollectionsReversalReasonRequired"));
            return;
        }
        if (!_dialogs.Confirm(Text("CollectionsReverseConfirm")))
            return;

        try
        {
            await _collectionService.ReverseCollectionAsync(SelectedCollection.Id, ReversalReason);
            ReversalReason = string.Empty;
            StatusMessage = Text("CollectionReversed");
            await LoadAsync();
            await LoadCollectionsForSelectedFeeAsync();
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public void PrintSelectedReceipt()
    {
        if (SelectedCollection is null || SelectedFee is null)
        {
            _dialogs.Warning(Text("CollectionsSelectActive"));
            return;
        }

        _dialogs.PrintCollectionReceipt(
            Companies.FirstOrDefault(company => company.Id == SelectedCollection.CompanyId)?.DisplayName ?? string.Empty,
            SelectedFee.Description,
            SelectedCollection.AmountReceived,
            SelectedFee.Currency,
            SelectedCollection.CollectionDate,
            SelectedCollection.PaymentMethod,
            SelectedCollection.ReceiptNumber);
    }

    private bool MatchesFilters(Fee fee)
    {
        if (SelectedStatusOption?.Value is FeeStatus status && fee.Status != status)
            return false;
        if (SelectedFiscalYearId.HasValue && fee.FiscalYearId != SelectedFiscalYearId)
            return false;
        if (FilterFromDate.HasValue && fee.FeeDate < FilterFromDate.Value)
            return false;
        if (FilterToDate.HasValue && fee.FeeDate > FilterToDate.Value)
            return false;
        return string.IsNullOrWhiteSpace(SearchText) || fee.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadFiscalYearsAsync()
    {
        var previousFiscalYearId = SelectedFiscalYearId;
        FiscalYears.Clear();
        if (SelectedCompanyId is not Guid companyId)
        {
            SelectedFiscalYearId = null;
            return;
        }
        foreach (var year in await _companyService.GetFiscalYearsAsync(companyId))
            FiscalYears.Add(year);
        SelectedFiscalYearId = previousFiscalYearId.HasValue && FiscalYears.Any(year => year.Id == previousFiscalYearId.Value)
            ? previousFiscalYearId
            : null;
    }

    [RelayCommand]
    public async Task AttachDocumentAsync()
    {
        if (SelectedFee is null) return;
        var company = Companies.FirstOrDefault(c => c.Id == SelectedFee.CompanyId);
        if (company is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _attachmentService.AttachAsync(company.Id, company.Code, "Fee", SelectedFee.Id, 10, dlg.FileName);
            StatusMessage = "Attached";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task OpenArchiveAsync()
    {
        if (SelectedCompanyId is not Guid companyId)
            return;

        var company = Companies.FirstOrDefault(item => item.Id == companyId);
        if (company is null)
            return;

        try
        {
            var path = await _archiveService.GetCompanyArchiveRootAsync(company.Id, company.Code);
            ShellService.OpenFolder(path);
            StatusMessage = string.Format(
                LocalizationService.Instance.CurrentCulture,
                Text("ArchiveOpenedOnDisk"),
                company.DisplayName);
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public async Task AttachCollectionDocumentAsync()
    {
        if (SelectedCollection is null) { _dialogs.Warning(Text("CollectionsSelectActive")); return; }
        var company = Companies.FirstOrDefault(item => item.Id == SelectedCollection.CompanyId);
        if (company is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await _attachmentService.AttachAsync(company.Id, company.Code, "Collection", SelectedCollection.Id, 10, dialog.FileName);
            StatusMessage = "Attached";
        }
        catch (Exception ex) { _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message)); }
    }

    private async Task LoadCollectionsForSelectedFeeAsync()
    {
        Collections.Clear();
        if (SelectedFee is null)
            return;

        var collections = await _feeService.GetCollectionsByFeeAsync(SelectedFee.Id);
        foreach (var collection in collections)
        {
            Collections.Add(new CollectionListItem
            {
                Id = collection.Id,
                CompanyId = collection.CompanyId,
                FeeId = collection.FeeId,
                FeeDescription = SelectedFee.Description,
                AmountReceived = collection.AmountReceived,
                PaymentMethod = collection.PaymentMethod,
                CollectionDate = collection.CollectionDate,
                ReceiptNumber = collection.ReceiptNumber,
                Notes = collection.Notes,
                IsReversed = collection.IsReversed,
                ReversalReason = collection.ReversalReason
            });
        }
    }

    partial void OnSelectedFeeChanged(FeeListItem? value) => _ = LoadCollectionsForSelectedFeeAsync();

    partial void OnSelectedCompanyIdChanged(Guid? oldValue, Guid? newValue)
    {
        if (oldValue == newValue)
            return;

        // Refreshing the bound Companies collection temporarily clears the selection.
        // Ignore that transient null so it is not mistaken for a user action (which
        // previously caused an endless LoadAsync recursion and a stack overflow).
        if (IsLoading && (oldValue is null || newValue is null))
            return;

        SelectedFiscalYearId = null;
        SelectedFee = null;
        SelectedCollection = null;
        _editingFeeId = null;
        _editingCollectionId = null;
        Collections.Clear();
        NewFee();
        if (IsLoading)
        {
            _companyReloadPending = true;
            return;
        }
        _ = LoadAsync();
    }

    private void RefreshFilterLabels()
    {
        foreach (var option in FilterStatuses)
            option.Label = option.Value is FeeStatus value ? Text($"FeeStatus{value}") : Text("CommonAll");
        if (PaymentMethod == "Cash" || PaymentMethod == "نقدي")
            PaymentMethod = Text("PaymentMethodCash");
        if (StatusMessage.Length > 0)
            StatusMessage = LocalizationService.Instance.TranslateMessage(StatusMessage);
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}

public partial class FeeStatusOption : ObservableObject
{
    public FeeStatusOption(FeeStatus? value, string label)
    {
        Value = value;
        Label = label;
    }

    public FeeStatus? Value { get; }
    [ObservableProperty] private string _label;
}
