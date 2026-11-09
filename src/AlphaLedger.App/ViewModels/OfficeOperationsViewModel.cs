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

public partial class OfficeOperationsViewModel : ObservableObject
{
    private readonly IOperationService _operationService;
    private readonly IFeeService _feeService;
    private readonly ICompanyService _companyService;
    private readonly IArchiveAttachmentService _attachmentService;
    private readonly ICompanyArchiveService _archiveService;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid? _initialCompanyId;
    private bool _companyReloadPending;
    public event Action<Guid>? OpenCompanyRequested;
    public event Action<Guid>? OpenArchiveRequested;

    public OfficeOperationsViewModel(
        IOperationService operationService,
        IFeeService feeService,
        ICompanyService companyService,
        IArchiveAttachmentService attachmentService,
        ICompanyArchiveService archiveService,
        WindowsDialogService dialogs,
        Guid companyId = default,
        Guid fiscalYearId = default)
    {
        _operationService = operationService;
        _feeService = feeService;
        _companyService = companyService;
        _attachmentService = attachmentService;
        _archiveService = archiveService;
        _dialogs = dialogs;
        _initialCompanyId = companyId == Guid.Empty ? null : companyId;
        SelectedCompanyId = _initialCompanyId;
        SelectedFiscalYearId = fiscalYearId == Guid.Empty ? null : fiscalYearId;
        FilterStatusOptions = new ObservableCollection<OperationStatusOption>(
            new[] { new OperationStatusOption(null, Text("CommonAll")) }
                .Concat(Enum.GetValues<OperationStatus>()
                    .Select(status => new OperationStatusOption(status, GetStatusText(status)))));
        SelectedFilterStatusOption = FilterStatusOptions[0];
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<OfficeOperation> Items { get; } = [];
    public ObservableCollection<OfficeOperation> OverdueItems { get; } = [];
    public ObservableCollection<OfficeOperation> UpcomingItems { get; } = [];
    public ObservableCollection<CompanyListItem> Companies { get; } = [];
    public ObservableCollection<Employee> Employees { get; } = [];
    public ObservableCollection<CompanyFiscalYear> FiscalYears { get; } = [];
    public ObservableCollection<OperationStatusOption> FilterStatusOptions { get; }
    public ObservableCollection<OperationStatus> OperationStatuses { get; } = new(Enum.GetValues<OperationStatus>());
    public ObservableCollection<OperationPriority> OperationPriorities { get; } = new(Enum.GetValues<OperationPriority>());

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showOverdueOnly;
    [ObservableProperty] private Guid? _selectedCompanyId;
    [ObservableProperty] private Guid? _selectedFiscalYearId;
    [ObservableProperty] private Guid? _filterEmployeeId;
    [ObservableProperty] private OperationStatusOption? _selectedFilterStatusOption;
    [ObservableProperty] private OperationStatus _operationStatus = OperationStatus.NotStarted;
    [ObservableProperty] private OperationPriority _operationPriority = OperationPriority.Medium;
    [ObservableProperty] private int _upcomingCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _notStartedCount;
    [ObservableProperty] private int _inProgressCount;
    [ObservableProperty] private int _waitingForDocumentsCount;
    [ObservableProperty] private int _underReviewCount;
    [ObservableProperty] private int _readyForDeliveryCount;
    [ObservableProperty] private int _submittedCount;
    [ObservableProperty] private int _completedCount;

    [ObservableProperty] private string _serviceCategory = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string? _taxPeriod;
    [ObservableProperty] private Guid? _assignedEmployeeId;
    [ObservableProperty] private DateOnly? _startDate;
    [ObservableProperty] private DateOnly? _dueDate;
    [ObservableProperty] private DateOnly? _completionDate;
    [ObservableProperty] private decimal _agreedFee;
    [ObservableProperty] private string? _notes;

    [ObservableProperty] private OfficeOperation? _selectedItem;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private Guid _editingId;

    public event Action? DataChanged;

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            Companies.Clear();
            foreach (var company in await _companyService.GetCompaniesAsync(new CompanyFilter()))
                Companies.Add(company);
            if (SelectedCompanyId is null && _initialCompanyId.HasValue && Companies.Any(company => company.Id == _initialCompanyId.Value))
                SelectedCompanyId = _initialCompanyId;
            if (SelectedCompanyId is null && Companies.Count > 0)
                SelectedCompanyId = Companies[0].Id;
            Employees.Clear();
            foreach (var employee in await _companyService.GetActiveEmployeesAsync())
                Employees.Add(employee);
            await LoadFiscalYearsAsync();
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
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

    private async Task LoadOperationsAsync()
    {
        Items.Clear();
        var ops = await _operationService.GetOperationsAsync(SelectedCompanyId);
        var yearOperations = ops.Where(operation =>
            !SelectedFiscalYearId.HasValue || operation.FiscalYearId == SelectedFiscalYearId).ToArray();
        TotalCount = yearOperations.Length;
        NotStartedCount = yearOperations.Count(operation => operation.Status == OperationStatus.NotStarted);
        InProgressCount = yearOperations.Count(operation => operation.Status == OperationStatus.InProgress);
        WaitingForDocumentsCount = yearOperations.Count(operation => operation.Status == OperationStatus.WaitingForDocuments);
        UnderReviewCount = yearOperations.Count(operation => operation.Status == OperationStatus.UnderReview);
        ReadyForDeliveryCount = yearOperations.Count(operation => operation.Status == OperationStatus.ReadyForDelivery);
        SubmittedCount = yearOperations.Count(operation => operation.Status == OperationStatus.SubmittedOrDelivered);
        CompletedCount = yearOperations.Count(operation => operation.Status == OperationStatus.Completed);
        foreach (var op in yearOperations)
        {
            if (MatchesFilter(op))
                Items.Add(op);
        }
    }

    private async Task LoadOverdueAsync()
    {
        OverdueItems.Clear();
        var overdue = await _operationService.GetOverdueOperationsAsync();
        foreach (var op in overdue)
        {
            if ((SelectedCompanyId is null || op.CompanyId == SelectedCompanyId) &&
                (!SelectedFiscalYearId.HasValue || op.FiscalYearId == SelectedFiscalYearId))
                OverdueItems.Add(op);
        }
    }

    private Task LoadUpcomingAsync()
    {
        UpcomingItems.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var limit = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
        foreach (var operation in Items.Where(item => item.DueDate >= today && item.DueDate <= limit && item.Status is not OperationStatus.Completed and not OperationStatus.Cancelled))
            UpcomingItems.Add(operation);
        UpcomingCount = UpcomingItems.Count;
        return Task.CompletedTask;
    }

    private bool MatchesFilter(OfficeOperation op)
    {
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !op.ServiceCategory.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) &&
            !op.Description.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (SelectedFilterStatusOption?.Value is OperationStatus status && op.Status != status)
            return false;
        if (SelectedFiscalYearId.HasValue && op.FiscalYearId != SelectedFiscalYearId)
            return false;
        if (FilterEmployeeId.HasValue && op.AssignedEmployeeId != FilterEmployeeId)
            return false;
        if (ShowOverdueOnly && (!op.DueDate.HasValue || op.DueDate.Value >= DateOnly.FromDateTime(DateTime.Today) || op.Status is OperationStatus.Completed or OperationStatus.Cancelled))
            return false;
        return true;
    }

    [RelayCommand]
    public async Task AddOperationAsync()
    {
        if (string.IsNullOrWhiteSpace(ServiceCategory))
        {
            _dialogs.Warning(Text("OperationsCategoryRequired"));
            return;
        }
        if (SelectedCompanyId is not Guid companyId)
        {
            _dialogs.Warning(Text("OperationsSelectCompany"));
            return;
        }
        try
        {
            var op = new OfficeOperation
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                FiscalYearId = SelectedFiscalYearId,
                ServiceCategory = ServiceCategory.Trim(),
                Description = Description,
                TaxPeriod = TaxPeriod,
                AssignedEmployeeId = AssignedEmployeeId,
                StartDate = StartDate,
                DueDate = DueDate,
                CompletionDate = CompletionDate,
                Status = OperationStatus,
                Priority = OperationPriority,
                AgreedFee = AgreedFee,
                Notes = Notes,
                CreatedAtUtc = DateTime.UtcNow
            };
            await _operationService.AddOperationAsync(op);
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
            ClearInputs();
            DataChanged?.Invoke();
            StatusMessage = Text("OperationsAdded");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task EditOperationAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("OperationsSelectFirst"));
            return;
        }
        IsEditMode = true;
        EditingId = SelectedItem.Id;
        SelectedCompanyId = SelectedItem.CompanyId;
        SelectedFiscalYearId = SelectedItem.FiscalYearId;
        ServiceCategory = SelectedItem.ServiceCategory;
        Description = SelectedItem.Description;
        TaxPeriod = SelectedItem.TaxPeriod;
        AssignedEmployeeId = SelectedItem.AssignedEmployeeId;
        StartDate = SelectedItem.StartDate;
        DueDate = SelectedItem.DueDate;
        CompletionDate = SelectedItem.CompletionDate;
        OperationStatus = SelectedItem.Status;
        OperationPriority = SelectedItem.Priority;
        AgreedFee = SelectedItem.AgreedFee;
        Notes = SelectedItem.Notes;
    }

    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (SelectedItem is null || !IsEditMode) return;
        try
        {
            var op = new OfficeOperation
            {
                Id = EditingId,
                CompanyId = SelectedItem.CompanyId,
                FiscalYearId = SelectedFiscalYearId,
                ServiceCategory = ServiceCategory.Trim(),
                Description = Description,
                TaxPeriod = TaxPeriod,
                AssignedEmployeeId = AssignedEmployeeId,
                StartDate = StartDate,
                DueDate = DueDate,
                CompletionDate = CompletionDate,
                Status = OperationStatus,
                Priority = OperationPriority,
                AgreedFee = AgreedFee,
                Notes = Notes,
                CreatedAtUtc = SelectedItem.CreatedAtUtc,
                ExternalReference = SelectedItem.ExternalReference
            };
            await _operationService.UpdateOperationAsync(EditingId, op);
            IsEditMode = false;
            ClearInputs();
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("OperationsUpdated");
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
    public async Task DeleteOperationAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("OperationsSelectFirst"));
            return;
        }
        if (!_dialogs.Confirm(Text("OperationsDeleteConfirm")))
            return;
        try
        {
            await _operationService.DeleteOperationAsync(SelectedItem.Id);
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("OperationsDeleted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task MarkCompleteAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("OperationsSelectFirst"));
            return;
        }

        try
        {
            var op = new OfficeOperation
            {
                Id = SelectedItem.Id,
                CompanyId = SelectedItem.CompanyId,
                FiscalYearId = SelectedItem.FiscalYearId,
                ServiceCategory = SelectedItem.ServiceCategory,
                Description = SelectedItem.Description,
                TaxPeriod = SelectedItem.TaxPeriod,
                AssignedEmployeeId = SelectedItem.AssignedEmployeeId,
                StartDate = SelectedItem.StartDate,
                DueDate = SelectedItem.DueDate,
                CompletionDate = DateOnly.FromDateTime(DateTime.Now),
                Status = OperationStatus.Completed,
                Priority = SelectedItem.Priority,
                AgreedFee = SelectedItem.AgreedFee,
                Notes = SelectedItem.Notes,
                CreatedAtUtc = SelectedItem.CreatedAtUtc,
                ExternalReference = SelectedItem.ExternalReference
            };
            await _operationService.UpdateOperationAsync(SelectedItem.Id, op);
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("OperationsMarkedCompleted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task FilterOperationsAsync()
    {
        await LoadFiscalYearsAsync();
        await LoadOperationsAsync();
        await LoadOverdueAsync();
        await LoadUpcomingAsync();
    }

    [RelayCommand]
    public async Task FilterByStatusAsync(OperationStatus status)
    {
        SelectedFilterStatusOption = FilterStatusOptions.FirstOrDefault(option => option.Value == status);
        ShowOverdueOnly = false;
        await LoadOperationsAsync();
        await LoadOverdueAsync();
        await LoadUpcomingAsync();
    }

    [RelayCommand]
    public async Task CreateFeeFromOperationAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("OperationsSelectFirst"));
            return;
        }
        if (SelectedItem.AgreedFee <= 0)
        {
            _dialogs.Warning(Text("OperationsFeeAmountRequired"));
            return;
        }

        try
        {
            var fees = await _feeService.GetFeesAsync(SelectedItem.CompanyId);
            if (fees.Any(fee => fee.OperationId == SelectedItem.Id && fee.Status != FeeStatus.Cancelled))
            {
                _dialogs.Warning(Text("OperationsFeeAlreadyCreated"));
                return;
            }

            await _feeService.AddFeeAsync(new Fee
            {
                Id = Guid.NewGuid(),
                CompanyId = SelectedItem.CompanyId,
                FiscalYearId = SelectedItem.FiscalYearId,
                OperationId = SelectedItem.Id,
                Description = $"{SelectedItem.ServiceCategory} - {SelectedItem.Description}".Trim(' ', '-'),
                FeeDate = DateOnly.FromDateTime(DateTime.Today),
                DueDate = SelectedItem.DueDate,
                Amount = SelectedItem.AgreedFee,
                Currency = "EGP",
                Status = FeeStatus.Agreed
            });
            StatusMessage = Text("OperationsFeeCreated");
            DataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogs.Error(LocalizationService.Instance.TranslateMessage(ex.Message));
        }
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

    [RelayCommand]
    public async Task ClearFilterAsync()
    {
        SearchText = string.Empty;
        SelectedFilterStatusOption = FilterStatusOptions[0];
        FilterEmployeeId = null;
        ShowOverdueOnly = false;
        await LoadOperationsAsync();
        await LoadOverdueAsync();
        await LoadUpcomingAsync();
    }

    private void ClearInputs()
    {
        ServiceCategory = string.Empty;
        Description = string.Empty;
        TaxPeriod = null;
        AssignedEmployeeId = null;
        StartDate = null;
        DueDate = null;
        CompletionDate = null;
        AgreedFee = 0;
        Notes = null;
    }

    private async Task LoadFiscalYearsAsync()
    {
        var previousId = SelectedFiscalYearId;
        FiscalYears.Clear();
        if (SelectedCompanyId is not Guid companyId)
        {
            SelectedFiscalYearId = null;
            return;
        }

        foreach (var year in await _companyService.GetFiscalYearsAsync(companyId))
            FiscalYears.Add(year);
        SelectedFiscalYearId = previousId.HasValue && FiscalYears.Any(year => year.Id == previousId.Value) ? previousId : null;
    }

    [RelayCommand]
    public async Task AttachDocumentAsync()
    {
        if (SelectedItem is null) return;
        var company = Companies.FirstOrDefault(c => c.Id == SelectedItem.CompanyId);
        if (company is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        int category = 1; // general
        var cat = SelectedItem.ServiceCategory ?? string.Empty;
        if (cat.Contains("فاتورة", StringComparison.OrdinalIgnoreCase) || cat.Contains("invoice", StringComparison.OrdinalIgnoreCase) || cat.Contains("expense", StringComparison.OrdinalIgnoreCase))
            category = 8;
        else if (cat.Contains("عقد", StringComparison.OrdinalIgnoreCase) || cat.Contains("contract", StringComparison.OrdinalIgnoreCase))
            category = 9;
        else if (cat.Contains("مراس", StringComparison.OrdinalIgnoreCase) || cat.Contains("letter", StringComparison.OrdinalIgnoreCase))
            category = 11;
        try
        {
            await _attachmentService.AttachAsync(company.Id, company.Code, "Operation", SelectedItem.Id, category, dlg.FileName);
            StatusMessage = "Attached";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void OnCultureChanged()
    {
        if (StatusMessage is not null)
            StatusMessage = LocalizationService.Instance.TranslateMessage(StatusMessage);
        foreach (var option in FilterStatusOptions)
            option.Label = option.Value is OperationStatus status ? GetStatusText(status) : Text("CommonAll");
    }

    private string GetStatusText(OperationStatus status) => status switch
    {
        OperationStatus.NotStarted => Text("OperationsStatusNotStarted"),
        OperationStatus.InProgress => Text("OperationsStatusInProgress"),
        OperationStatus.WaitingForDocuments => Text("OperationsStatusWaitingDocuments"),
        OperationStatus.UnderReview => Text("OperationsStatusUnderReview"),
        OperationStatus.ReadyForDelivery => Text("OperationsStatusReadyForDelivery"),
        OperationStatus.SubmittedOrDelivered => Text("OperationsStatusSubmitted"),
        OperationStatus.Completed => Text("OperationsStatusCompleted"),
        OperationStatus.Cancelled => Text("OperationsStatusCancelled"),
        _ => Text("CommonUnknown")
    };

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
        ClearInputs();
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
            await LoadOperationsAsync();
            await LoadOverdueAsync();
            await LoadUpcomingAsync();
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
}

public partial class OperationStatusOption : ObservableObject
{
    public OperationStatusOption(OperationStatus? value, string label)
    {
        Value = value;
        Label = label;
    }

    public OperationStatus? Value { get; }
    [ObservableProperty] private string _label;
}
