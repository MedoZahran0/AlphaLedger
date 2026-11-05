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

public partial class FiscalYearsViewModel : ObservableObject
{
    private readonly IFiscalYearService _fiscalYearService;
    private readonly ICompanyService _companyService;
    private readonly WindowsDialogService _dialogs;
    private Guid? _companyId;

    public FiscalYearsViewModel(
        IFiscalYearService fiscalYearService,
        ICompanyService companyService,
        WindowsDialogService dialogs,
        Guid companyId = default)
    {
        _fiscalYearService = fiscalYearService;
        _companyService = companyService;
        _dialogs = dialogs;
        _companyId = companyId == Guid.Empty ? null : companyId;
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<CompanyFiscalYear> Items { get; } = [];
    public ObservableCollection<CompanyListItem> Companies { get; } = [];

    [ObservableProperty] private Guid? _selectedCompanyId;

    [ObservableProperty] private string? _companyName;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _yearLabel = string.Empty;
    [ObservableProperty] private DateOnly _startDate = new DateOnly(DateTime.Now.Year, 1, 1);
    [ObservableProperty] private DateOnly _endDate = new DateOnly(DateTime.Now.Year, 12, 31);
    [ObservableProperty] private FiscalYearStatus _status = FiscalYearStatus.Active;
    [ObservableProperty] private string? _notes;

    [ObservableProperty] private CompanyFiscalYear? _selectedItem;
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
            foreach (var company in companies) Companies.Add(company);
            if (_companyId.HasValue && Companies.Any(company => company.Id == _companyId.Value))
                SelectedCompanyId = _companyId;
            else if (Companies.Count > 0)
                SelectedCompanyId = Companies[0].Id;
            await LoadYearsAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadYearsAsync()
    {
        Items.Clear();
        if (SelectedCompanyId is not Guid companyId)
        {
            CompanyName = string.Empty;
            return;
        }
        _companyId = companyId;
        CompanyName = Companies.FirstOrDefault(company => company.Id == companyId)?.DisplayName ?? string.Empty;
        var years = await _fiscalYearService.GetFiscalYearsAsync(companyId);
        foreach (var y in years)
            Items.Add(y);
    }

    partial void OnSelectedCompanyIdChanged(Guid? value)
    {
        _companyId = value;
        IsEditMode = false;
        ClearInputs();
        if (value.HasValue) _ = LoadYearsAsync(); else Items.Clear();
    }

    [RelayCommand]
    public async Task AddYearAsync()
    {
        if (_companyId is not Guid companyId) { _dialogs.Warning(Text("FeesSelectCompany")); return; }
        if (!ValidateYearInput()) return;
        try
        {
            var model = new FiscalYearEditModel
            {
                YearLabel = YearLabel.Trim(),
                StartDate = StartDate,
                EndDate = EndDate,
                Status = Status,
                Notes = Notes
            };
            await _fiscalYearService.AddFiscalYearAsync(companyId, model);
            await LoadYearsAsync();
            ClearInputs();
            DataChanged?.Invoke();
            StatusMessage = Text("FiscalYearsYearAdded");
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task EditYearAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("FiscalYearsSelectFirst"));
            return;
        }
        if (SelectedItem.Status == FiscalYearStatus.Closed)
        {
            _dialogs.Warning(Text("FiscalYearsCannotEditClosed"));
            return;
        }
        IsEditMode = true;
        EditingId = SelectedItem.Id;
        YearLabel = SelectedItem.YearLabel;
        StartDate = SelectedItem.StartDate;
        EndDate = SelectedItem.EndDate;
        Status = SelectedItem.Status;
        Notes = SelectedItem.Notes;
    }

    [RelayCommand]
    public async Task SaveEditAsync()
    {
        if (SelectedItem is null || !IsEditMode) return;
        if (!ValidateYearInput()) return;
        try
        {
            var model = new FiscalYearEditModel
            {
                YearLabel = YearLabel.Trim(),
                StartDate = StartDate,
                EndDate = EndDate,
                Status = Status,
                Notes = Notes
            };
            await _fiscalYearService.UpdateFiscalYearAsync(EditingId, model);
            IsEditMode = false;
            ClearInputs();
            await LoadYearsAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("FiscalYearsYearUpdated");
        }
        catch (BusinessException ex)
        {
            _dialogs.Error(ex.Message);
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
    public async Task CloseYearAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("FiscalYearsSelectFirst"));
            return;
        }
        if (SelectedItem.Status == FiscalYearStatus.Closed)
        {
            _dialogs.Warning(Text("FiscalYearsAlreadyClosed"));
            return;
        }
        if (!_dialogs.Confirm(string.Format(LocalizationService.Instance.CurrentCulture,
            Text("FiscalYearsConfirmClose"), SelectedItem.YearLabel)))
            return;
        try
        {
            await _fiscalYearService.CloseFiscalYearAsync(SelectedItem.Id);
            await LoadYearsAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("FiscalYearsYearClosed");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task ReopenYearAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("FiscalYearsSelectFirst"));
            return;
        }
        if (SelectedItem.Status == FiscalYearStatus.Active)
        {
            _dialogs.Warning(Text("FiscalYearsAlreadyActive"));
            return;
        }
        try
        {
            await _fiscalYearService.ReopenFiscalYearAsync(SelectedItem.Id);
            await LoadYearsAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("FiscalYearsYearReopened");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task DeleteYearAsync()
    {
        if (SelectedItem is null)
        {
            _dialogs.Warning(Text("FiscalYearsSelectFirst"));
            return;
        }
        if (!_dialogs.Confirm(string.Format(LocalizationService.Instance.CurrentCulture,
            Text("FiscalYearsConfirmDelete"), SelectedItem.YearLabel)))
            return;
        try
        {
            await _fiscalYearService.DeleteFiscalYearAsync(SelectedItem.Id);
            await LoadYearsAsync();
            DataChanged?.Invoke();
            StatusMessage = Text("FiscalYearsYearDeleted");
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    private bool ValidateYearInput()
    {
        if (string.IsNullOrWhiteSpace(YearLabel))
        {
            _dialogs.Warning(Text("FiscalYearsYearLabelRequired"));
            return false;
        }
        if (EndDate <= StartDate)
        {
            _dialogs.Warning(Text("FiscalYearsEndMustFollowStart"));
            return false;
        }
        return true;
    }

    private void ClearInputs()
    {
        YearLabel = string.Empty;
        StartDate = new DateOnly(DateTime.Now.Year, 1, 1);
        EndDate = new DateOnly(DateTime.Now.Year, 12, 31);
        Status = FiscalYearStatus.Active;
        Notes = null;
    }

    public string GetStatusText(FiscalYearStatus status) => status switch
    {
        FiscalYearStatus.Planned => Text("FiscalYearsStatusPlanned"),
        FiscalYearStatus.Active => Text("FiscalYearsStatusActive"),
        FiscalYearStatus.Closed => Text("FiscalYearsStatusClosed"),
        _ => Text("CommonUnknown")
    };

    private void OnCultureChanged()
    {
        if (StatusMessage is not null)
            StatusMessage = LocalizationService.Instance.TranslateMessage(StatusMessage);
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
