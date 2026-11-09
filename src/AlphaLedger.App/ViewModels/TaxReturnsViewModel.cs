using System.Collections.ObjectModel;
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

public partial class TaxReturnsViewModel : ObservableObject
{
    private readonly ITaxReturnService _taxReturnService;
    private readonly ICompanyService _companyService;
    private readonly IOperationService _operationService;
    private readonly IFeeService _feeService;
    private readonly IArchiveAttachmentService _attachmentService;
    private readonly ICompanyArchiveService _archiveService;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid? _initialCompanyId;
    private bool _companyReloadPending;
    public event Action<Guid>? OpenCompanyRequested;
    public event Action<Guid>? OpenArchiveRequested;

    public TaxReturnsViewModel(
        ITaxReturnService taxReturnService,
        ICompanyService companyService,
        IOperationService operationService,
        IFeeService feeService,
        IArchiveAttachmentService attachmentService,
        ICompanyArchiveService archiveService,
        WindowsDialogService dialogs,
        Guid companyId = default,
        Guid fiscalYearId = default)
    {
        _taxReturnService = taxReturnService;
        _companyService = companyService;
        _operationService = operationService;
        _feeService = feeService;
        _attachmentService = attachmentService;
        _archiveService = archiveService;
        _dialogs = dialogs;
        _initialCompanyId = companyId == Guid.Empty ? null : companyId;
        SelectedCompanyId = _initialCompanyId;
        SelectedFiscalYearId = fiscalYearId == Guid.Empty ? null : fiscalYearId;
        PreparationStatus = Text("OperationsStatusNotStarted");
        ReviewStatus = Text("TaxReturnsReviewStatusNotReviewed");
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
        TaxTypes = new ObservableCollection<TaxTypeOption>
        {
            new("VAT", Text("TaxReturnsTypeVAT")),
            new("IncomeTax", Text("TaxReturnsTypeIncomeTax")),
            new("WithholdingTax", Text("TaxReturnsTypeWithholding")),
            new("PayrollTax", Text("TaxReturnsTypePayroll"))
        };
        FilingStatuses = new ObservableCollection<TaxReturnFilingStatus>(
            Enum.GetValues<TaxReturnFilingStatus>().Where(status => status != TaxReturnFilingStatus.Overdue));
        FilterStatusOptions = new ObservableCollection<TaxReturnStatusOption>(
            new[] { new TaxReturnStatusOption(null, Text("CommonAll")) }
                .Concat(Enum.GetValues<TaxReturnFilingStatus>().Select(status => new TaxReturnStatusOption(status, GetStatusText(status)))));
        SelectedFilterStatus = FilterStatusOptions[0];
    }

    public ObservableCollection<TaxReturn> Items { get; } = [];
    public ObservableCollection<TaxReturn> OverdueItems { get; } = [];
    public ObservableCollection<CompanyListItem> Companies { get; } = [];
    public ObservableCollection<CompanyFiscalYear> FiscalYears { get; } = [];
    public ObservableCollection<Employee> Employees { get; } = [];
    public ObservableCollection<OfficeOperation> Operations { get; } = [];
    public ObservableCollection<Fee> Fees { get; } = [];
    public ObservableCollection<TaxTypeOption> TaxTypes { get; }
    public ObservableCollection<TaxReturnFilingStatus> FilingStatuses { get; }
    public ObservableCollection<TaxReturnStatusOption> FilterStatusOptions { get; }

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _notStartedCount;
    [ObservableProperty] private int _inPreparationCount;
    [ObservableProperty] private int _awaitingReviewCount;
    [ObservableProperty] private int _readyToFileCount;
    [ObservableProperty] private int _submittedCount;
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _filterTaxType = string.Empty;
    [ObservableProperty] private TaxReturnFilingStatus _filterStatus;
    [ObservableProperty] private bool _showOverdueOnly;
    [ObservableProperty] private Guid? _selectedCompanyId;
    [ObservableProperty] private Guid? _selectedFiscalYearId;
    [ObservableProperty] private TaxReturnStatusOption? _selectedFilterStatus;
    [ObservableProperty] private TaxTypeOption? _selectedTaxTypeOption;
    [ObservableProperty] private TaxTypeOption? _selectedFilterTaxTypeOption;

    [ObservableProperty] private string _taxType = string.Empty;
    [ObservableProperty] private string _taxPeriod = string.Empty;
    [ObservableProperty] private DateOnly _filingDueDate = DateOnly.FromDateTime(DateTime.Now);
    [ObservableProperty] private string _preparationStatus = string.Empty;
    [ObservableProperty] private string _reviewStatus = string.Empty;
    [ObservableProperty] private TaxReturnFilingStatus _filingStatus = TaxReturnFilingStatus.NotStarted;
    [ObservableProperty] private DateOnly? _submissionDate;
    [ObservableProperty] private string? _officialReceiptNumber;
    [ObservableProperty] private Guid? _responsibleEmployeeId;
    [ObservableProperty] private Guid? _relatedOperationId;
    [ObservableProperty] private Guid? _relatedFeeId;
    [ObservableProperty] private string? _notes;

    [ObservableProperty] private TaxReturn? _selectedItem;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private Guid _editingId;

    public event Action? DataChanged;

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var companies = await _companyService.GetCompaniesAsync(new CompanyFilter());
            Companies.Clear();
            foreach (var company in companies)
                Companies.Add(company);
            if (SelectedCompanyId is null && _initialCompanyId is Guid initialCompanyId && Companies.Any(company => company.Id == initialCompanyId))
                SelectedCompanyId = initialCompanyId;
            if (SelectedCompanyId is null && Companies.Count > 0)
                SelectedCompanyId = Companies[0].Id;
            await LoadFiscalYearsAsync();
            await LoadReferenceDataAsync();
            if (SelectedTaxTypeOption is null && TaxTypes.Count > 0)
                SelectedTaxTypeOption = TaxTypes[0];
            if (FilterStatusOptions.Count > 0 && SelectedFilterStatus is null)
                SelectedFilterStatus = FilterStatusOptions[0];
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
        finally
        {
            IsLoading = false;
            if (_companyReloadPending)
            {
                _companyReloadPending = false;
                _ = ReloadCompanyDataAsync();
            }
        }
    }

    private async Task LoadTaxReturnsAsync()
    {
        Items.Clear();
        var returns = await _taxReturnService.GetTaxReturnsAsync(SelectedCompanyId);
        var yearReturns = returns.Where(item =>
            !SelectedFiscalYearId.HasValue || item.FiscalYearId == SelectedFiscalYearId).ToArray();
        foreach (var taxReturn in yearReturns)
            taxReturn.EffectiveFilingStatus = GetEffectiveStatus(taxReturn);
        UpdateStatusCounts(yearReturns);
        foreach (var r in yearReturns)
        {
            if (MatchesFilter(r))
                Items.Add(r);
        }
    }

    private void UpdateStatusCounts(IEnumerable<TaxReturn> returns)
    {
        var allReturns = returns.ToArray();
        TotalCount = allReturns.Length;
        NotStartedCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.NotStarted);
        InPreparationCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.InPreparation);
        AwaitingReviewCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.AwaitingReview);
        ReadyToFileCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.ReadyToFile);
        SubmittedCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.Submitted);
        OverdueCount = allReturns.Count(item => GetEffectiveStatus(item) == TaxReturnFilingStatus.Overdue);
    }

    private static TaxReturnFilingStatus GetEffectiveStatus(TaxReturn taxReturn)
    {
        if (taxReturn.FilingStatus == TaxReturnFilingStatus.Submitted)
            return TaxReturnFilingStatus.Submitted;
        return taxReturn.FilingDueDate < DateOnly.FromDateTime(DateTime.Today)
            ? TaxReturnFilingStatus.Overdue
            : taxReturn.FilingStatus == TaxReturnFilingStatus.Overdue
                ? TaxReturnFilingStatus.NotStarted
                : taxReturn.FilingStatus;
    }

    private async Task LoadReferenceDataAsync()
    {
        Employees.Clear();
        Operations.Clear();
        Fees.Clear();
        if (SelectedCompanyId is not Guid companyId)
        {
            ResponsibleEmployeeId = null;
            RelatedOperationId = null;
            RelatedFeeId = null;
            return;
        }

        foreach (var employee in await _companyService.GetActiveEmployeesAsync())
            Employees.Add(employee);
        foreach (var operation in (await _operationService.GetOperationsAsync(companyId))
                     .Where(item => !SelectedFiscalYearId.HasValue || item.FiscalYearId == SelectedFiscalYearId))
            Operations.Add(operation);
        foreach (var fee in (await _feeService.GetFeesAsync(companyId))
                     .Where(item => !SelectedFiscalYearId.HasValue || item.FiscalYearId == SelectedFiscalYearId))
            Fees.Add(fee);

        if (ResponsibleEmployeeId.HasValue && !Employees.Any(item => item.Id == ResponsibleEmployeeId.Value))
            ResponsibleEmployeeId = null;
        if (RelatedOperationId.HasValue && !Operations.Any(item => item.Id == RelatedOperationId.Value))
            RelatedOperationId = null;
        if (RelatedFeeId.HasValue && !Fees.Any(item => item.Id == RelatedFeeId.Value))
            RelatedFeeId = null;
    }

    private async Task LoadOverdueAsync()
    {
        OverdueItems.Clear();
        var overdue = await _taxReturnService.GetOverdueTaxReturnsAsync();
        foreach (var r in overdue)
        {
            if ((SelectedCompanyId is null || r.CompanyId == SelectedCompanyId) &&
                (!SelectedFiscalYearId.HasValue || r.FiscalYearId == SelectedFiscalYearId))
            {
                r.EffectiveFilingStatus = GetEffectiveStatus(r);
                OverdueItems.Add(r);
            }
        }
    }

    private bool MatchesFilter(TaxReturn r)
    {
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !r.TaxType.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) &&
            !r.TaxPeriod.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(FilterTaxType) && !TaxTypeMatches(r.TaxType, FilterTaxType))
            return false;
        if (SelectedFilterStatus?.Value is TaxReturnFilingStatus status && GetEffectiveStatus(r) != status)
            return false;
        if (SelectedFiscalYearId.HasValue && r.FiscalYearId != SelectedFiscalYearId)
            return false;
        if (ShowOverdueOnly && GetEffectiveStatus(r) != TaxReturnFilingStatus.Overdue)
            return false;
        return true;
    }

    [RelayCommand]
    public async Task FilterByStatusAsync(TaxReturnFilingStatus status)
    {
        ShowOverdueOnly = status == TaxReturnFilingStatus.Overdue;
        SelectedFilterStatus = FilterStatusOptions.FirstOrDefault(option => option.Value == status)
            ?? FilterStatusOptions[0];
        await LoadTaxReturnsAsync();
    }

    [RelayCommand]
    public async Task AddTaxReturnAsync()
    {
        if (string.IsNullOrWhiteSpace(TaxType))
        {
            _dialogs.Warning(Text("TaxReturnsTypeRequired"));
            return;
        }
        if (SelectedCompanyId is not Guid companyId)
        {
            _dialogs.Warning(Text("TaxReturnsSelectCompany"));
            return;
        }
        try
        {
            var tr = new TaxReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                FiscalYearId = SelectedFiscalYearId,
                TaxType = SelectedTaxTypeOption?.Code ?? TaxType.Trim(),
                TaxPeriod = TaxPeriod,
                FilingDueDate = FilingDueDate,
                PreparationStatus = PreparationStatus,
                ReviewStatus = ReviewStatus,
                FilingStatus = FilingStatus,
                SubmissionDate = SubmissionDate,
                OfficialReceiptNumber = OfficialReceiptNumber,
                ResponsibleEmployeeId = ResponsibleEmployeeId,
                RelatedOperationId = RelatedOperationId,
                RelatedFeeId = RelatedFeeId,
                Notes = Notes,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await _taxReturnService.AddTaxReturnAsync(tr);
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
            ClearInputs();
            DataChanged?.Invoke();
            StatusMessage = Text("TaxReturnsAdded");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task EditTaxReturnAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("TaxReturnsSelectFirst"));
            return;
        }
        IsEditMode = true;
        EditingId = SelectedItem.Id;
        SelectedTaxTypeOption = FindTaxTypeOption(SelectedItem.TaxType);
        TaxType = SelectedItem.TaxType;
        TaxPeriod = SelectedItem.TaxPeriod;
        FilingDueDate = SelectedItem.FilingDueDate;
        PreparationStatus = SelectedItem.PreparationStatus;
        ReviewStatus = SelectedItem.ReviewStatus;
        FilingStatus = SelectedItem.FilingStatus;
        SubmissionDate = SelectedItem.SubmissionDate;
        OfficialReceiptNumber = SelectedItem.OfficialReceiptNumber;
        ResponsibleEmployeeId = SelectedItem.ResponsibleEmployeeId;
        RelatedOperationId = SelectedItem.RelatedOperationId;
        RelatedFeeId = SelectedItem.RelatedFeeId;
        Notes = SelectedItem.Notes;
    }

    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (SelectedItem is null || !IsEditMode) return;
        try
        {
            var tr = new TaxReturn
            {
                Id = EditingId,
                CompanyId = SelectedItem.CompanyId,
                FiscalYearId = SelectedFiscalYearId,
                TaxType = SelectedTaxTypeOption?.Code ?? TaxType.Trim(),
                TaxPeriod = TaxPeriod,
                FilingDueDate = FilingDueDate,
                PreparationStatus = PreparationStatus,
                ReviewStatus = ReviewStatus,
                FilingStatus = FilingStatus,
                SubmissionDate = SubmissionDate,
                OfficialReceiptNumber = OfficialReceiptNumber,
                ResponsibleEmployeeId = ResponsibleEmployeeId,
                RelatedOperationId = RelatedOperationId,
                RelatedFeeId = RelatedFeeId,
                Notes = Notes,
                CreatedAtUtc = SelectedItem.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await _taxReturnService.UpdateTaxReturnAsync(EditingId, tr);
            IsEditMode = false;
            ClearInputs();
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("TaxReturnsUpdated");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public void CancelEditAsync()
    {
        IsEditMode = false;
        ClearInputs();
    }

    [RelayCommand]
    public async Task DeleteTaxReturnAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("TaxReturnsSelectFirst"));
            return;
        }
        if (!_dialogs.Confirm(Text("TaxReturnsDeleteConfirm")))
            return;
        try
        {
            await _taxReturnService.DeleteTaxReturnAsync(SelectedItem.Id);
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("TaxReturnsDeleted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task MarkSubmittedAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("TaxReturnsSelectFirst"));
            return;
        }
        try
        {
            await _taxReturnService.MarkSubmittedAsync(SelectedItem.Id, OfficialReceiptNumber);
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("TaxReturnsReturnSubmitted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task FilterTaxReturnsAsync()
    {
        await LoadTaxReturnsAsync();
    }

    [RelayCommand]
    public async Task ClearFilterAsync()
    {
        SearchText = string.Empty;
        FilterTaxType = string.Empty;
        SelectedFilterTaxTypeOption = null;
        SelectedFilterStatus = FilterStatusOptions[0];
        ShowOverdueOnly = false;
        await LoadTaxReturnsAsync();
    }

    private void ClearInputs()
    {
        TaxType = string.Empty;
        TaxPeriod = string.Empty;
        FilingDueDate = DateOnly.FromDateTime(DateTime.Now);
        PreparationStatus = Text("OperationsStatusNotStarted");
        ReviewStatus = Text("TaxReturnsReviewStatusNotReviewed");
        FilingStatus = TaxReturnFilingStatus.NotStarted;
        SubmissionDate = null;
        OfficialReceiptNumber = null;
        ResponsibleEmployeeId = null;
        RelatedOperationId = null;
        RelatedFeeId = null;
        Notes = null;
    }

    private async Task LoadFiscalYearsAsync()
    {
        var priorId = SelectedFiscalYearId;
        FiscalYears.Clear();
        if (SelectedCompanyId is not Guid companyId)
        {
            SelectedFiscalYearId = null;
            return;
        }

        foreach (var year in await _companyService.GetFiscalYearsAsync(companyId))
            FiscalYears.Add(year);
        SelectedFiscalYearId = priorId.HasValue && FiscalYears.Any(year => year.Id == priorId.Value) ? priorId : null;
    }

    private bool TaxTypeMatches(string storedType, string selectedCode)
    {
        if (string.Equals(storedType, selectedCode, StringComparison.OrdinalIgnoreCase))
            return true;
        var option = TaxTypes.FirstOrDefault(item => item.Code == selectedCode);
        return option is not null && string.Equals(storedType, option.Label, StringComparison.OrdinalIgnoreCase);
    }

    private TaxTypeOption FindTaxTypeOption(string storedType)
    {
        var option = TaxTypes.FirstOrDefault(item => TaxTypeMatches(storedType, item.Code));
        if (option is not null)
            return option;
        var custom = new TaxTypeOption(storedType, storedType);
        TaxTypes.Add(custom);
        return custom;
    }

    partial void OnSelectedTaxTypeOptionChanged(TaxTypeOption? value)
    {
        if (value is not null)
            TaxType = value.Code;
    }

    partial void OnSelectedFilterTaxTypeOptionChanged(TaxTypeOption? value)
    {
        FilterTaxType = value?.Code ?? string.Empty;
    }

    [RelayCommand]
    public async Task FilterAsync()
    {
        await LoadFiscalYearsAsync();
        await LoadReferenceDataAsync();
        await LoadTaxReturnsAsync();
        await LoadOverdueAsync();
    }

    public string GetStatusText(TaxReturnFilingStatus status) => status switch
    {
        TaxReturnFilingStatus.NotStarted => Text("TaxReturnsStatusNotStarted"),
        TaxReturnFilingStatus.InPreparation => Text("TaxReturnsStatusInPreparation"),
        TaxReturnFilingStatus.AwaitingReview => Text("TaxReturnsStatusAwaitingReview"),
        TaxReturnFilingStatus.ReadyToFile => Text("TaxReturnsStatusReadyToFile"),
        TaxReturnFilingStatus.Submitted => Text("TaxReturnsStatusSubmitted"),
        TaxReturnFilingStatus.Overdue => Text("TaxReturnsStatusOverdue"),
        _ => Text("CommonUnknown")
    };

    [RelayCommand]
    public async Task AttachDocumentAsync()
    {
        if (SelectedItem is null || SelectedCompanyId is null) return;
        var company = Companies.FirstOrDefault(c => c.Id == SelectedCompanyId.Value);
        if (company is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        int category = SelectedItem.TaxType switch
        {
            "VAT" => 3,
            "WithholdingTax" => 4,
            "IncomeTax" => 5,
            "PayrollTax" => 6,
            _ => 12
        };
        try
        {
            await _attachmentService.AttachAsync(company.Id, company.Code, "TaxReturn", SelectedItem.Id, category, dlg.FileName);
            StatusMessage = "Attached";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void OnCultureChanged()
    {
        if (StatusMessage is not null)
            StatusMessage = LocalizationService.Instance.TranslateMessage(StatusMessage);

        PreparationStatus = LocalizationService.Instance.TranslateMessage(PreparationStatus);
        ReviewStatus = LocalizationService.Instance.TranslateMessage(ReviewStatus);
    }

    partial void OnSelectedCompanyIdChanged(Guid? oldValue, Guid? newValue)
    {
        if (oldValue == newValue)
            return;

        // Clearing the bound Companies collection temporarily nulls the selection.
        // Ignore that transient change while loading so it does not trigger a reload loop.
        if (IsLoading && (oldValue is null || newValue is null))
            return;
        SelectedFiscalYearId = null;
        SelectedItem = null;
        IsEditMode = false;
        if (IsLoading)
        {
            _companyReloadPending = true;
            return;
        }
        _ = ReloadCompanyDataAsync();
    }

    private async Task ReloadCompanyDataAsync()
    {
        IsLoading = true;
        try
        {
            await LoadFiscalYearsAsync();
            await LoadReferenceDataAsync();
            await LoadTaxReturnsAsync();
            await LoadOverdueAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
        finally
        {
            IsLoading = false;
            if (_companyReloadPending)
            {
                _companyReloadPending = false;
                _ = ReloadCompanyDataAsync();
            }
        }
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);

    partial void OnRelatedOperationIdChanged(Guid? value)
    {
        if (value is not Guid operationId)
            return;
        var operation = Operations.FirstOrDefault(item => item.Id == operationId);
        if (operation?.FiscalYearId is Guid fiscalYearId)
            SelectedFiscalYearId = fiscalYearId;
        var matchingFee = Fees.FirstOrDefault(item => item.OperationId == operationId);
        if (matchingFee is not null)
            RelatedFeeId = matchingFee.Id;
    }

    [RelayCommand]
    public void OpenSelectedCompany()
    {
        if (SelectedCompanyId is Guid companyId)
            OpenCompanyRequested?.Invoke(companyId);
    }

    [RelayCommand]
    public async Task OpenSelectedArchiveAsync()
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
}

public sealed record TaxReturnStatusOption(TaxReturnFilingStatus? Value, string Label);
public sealed record TaxTypeOption(string Code, string Label);
