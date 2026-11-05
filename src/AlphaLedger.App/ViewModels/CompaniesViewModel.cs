using System.Collections.ObjectModel;
using System.Globalization;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class CompaniesViewModel : ObservableObject
{
    private readonly ICompanyService _companies;
    private readonly ICompanyArchiveService _archive;
    private readonly ICompanyExcelExporter _excel;
    private readonly WindowsDialogService _dialogs;
    private readonly WindowsFolderPicker _picker;
    private readonly Action<Guid, string> _openProfile;
    private readonly Action<Guid> _openArchive;

    public CompaniesViewModel(
        ICompanyService companies,
        ICompanyArchiveService archive,
        ICompanyExcelExporter excel,
        WindowsDialogService dialogs,
        WindowsFolderPicker picker,
        Action<Guid, string> openProfile,
        Action<Guid> openArchive)
    {
        _companies = companies;
        _archive = archive;
        _excel = excel;
        _dialogs = dialogs;
        _picker = picker;
        _openProfile = openProfile;
        _openArchive = openArchive;
        Status = Text("CommonReadyStatus");
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<CompanyListItem> Items { get; } = [];
    public ObservableCollection<Employee> Employees { get; } = [];

    [ObservableProperty] private CompanyListItem? _selected;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _includeArchived;
    [ObservableProperty] private Guid? _selectedEmployeeId;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private int _count;

    public event Action<CompanyListItem>? RequestEdit;
    public event Action? RequestAdd;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            await ReloadItemsAsync();
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    public async Task LoadEmployeesAsync()
    {
        try
        {
            var emps = await _companies.GetActiveEmployeesAsync();
            Employees.Clear();
            Employees.Add(new Employee { Id = Guid.Empty, FullName = Text("CompaniesAllEmployees") });
            foreach (var emp in emps)
            {
                Employees.Add(emp);
            }
        }
        catch
        {
            // Ignore employee loading error if any
        }
    }

    [RelayCommand]
    public void Add() => RequestAdd?.Invoke();

    [RelayCommand]
    public void Edit()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        RequestEdit?.Invoke(Selected);
    }

    [RelayCommand]
    public void OpenProfile()
    {
        if (Selected is null)
        {
            return;
        }

        _openProfile(Selected.Id, Selected.DisplayName);
    }

    [RelayCommand]
    public async Task ArchiveAsync()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        if (!_dialogs.Confirm(string.Format(CultureInfo.CurrentCulture, Text("CompaniesArchiveConfirm"), Selected.DisplayName)))
        {
            return;
        }

        await _companies.ArchiveCompanyAsync(Selected.Id);
        await LoadAsync();
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        await _companies.RestoreCompanyAsync(Selected.Id);
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        var company = Selected;
        if (company is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        if (!_dialogs.Confirm(string.Format(CultureInfo.CurrentCulture, Text("CompaniesDeleteConfirm"), company.DisplayName)))
        {
            return;
        }

        try
        {
            await _companies.DeleteCompanyAsync(company.Id);
            Items.Remove(company);
            Selected = null;
            Count = Items.Count;
            Status = string.Format(CultureInfo.CurrentCulture,
                Text(IncludeArchived ? "CompaniesRecordCount" : "CompaniesActiveCount"), Count);
            _dialogs.Info(Text("CompaniesDeleted"));
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }
        _openArchive(Selected.Id);
    }

    [RelayCommand]
    public async Task OpenFolderOnDiskAsync()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        try
        {
            var path = await _archive.GetCompanyArchiveRootAsync(Selected.Id, Selected.Code);
            ShellService.OpenFolder(path);
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task CreateArchiveFoldersAsync()
    {
        var company = Selected;
        if (company is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        try
        {
            await _archive.EnsureCompanyFoldersAsync(company.Id, company.Code);
            await ReloadItemsAsync();
            Selected = Items.FirstOrDefault(item => item.Id == company.Id);
            _dialogs.Info(string.Format(
                CultureInfo.CurrentCulture,
                Text("CompaniesArchiveFoldersCreated"),
                company.DisplayName));
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task LocateFolderAsync()
    {
        if (Selected is null)
        {
            _dialogs.Warning(Text("CompaniesSelectCompany"));
            return;
        }

        var path = _picker.PickFolder(Text("CompaniesPickMissingFolder"), Selected.DocumentStoragePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _companies.RelocateCompanyFolderAsync(Selected.Id, path);
            await LoadAsync();
            _dialogs.Info(Text("CompaniesFolderUpdated"));
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    [RelayCommand]
    public async Task ExportExcelAsync()
    {
        try
        {
            var path = _dialogs.SaveExcel(string.Format(CultureInfo.CurrentCulture, Text("CompaniesExportFileName"), DateTime.Now));
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var headers = new[]
            {
                Text("CompaniesHeaderCode"),
                Text("CompaniesHeaderName"),
                Text("CompaniesHeaderLegalName"),
                Text("CompaniesHeaderTaxRegistration"),
                Text("CompaniesHeaderActivity"),
                Text("CompaniesEmployeeFilter"),
                Text("CompaniesHeaderOperationsCount"),
                Text("CompaniesHeaderAgreedFees"),
                Text("CompaniesHeaderCollections"),
                Text("CompaniesHeaderRemaining"),
                Text("CompaniesHeaderOverdueOperations"),
                Text("CompaniesHeaderNextTaxDeadline"),
                Text("CompaniesHeaderStatus")
            };
            await _excel.ExportAsync(
                Items.ToList(),
                path,
                headers,
                Text("CompaniesSheetName"),
                Text("CompaniesStatusActive"),
                Text("CompaniesStatusArchived"),
                LocalizationService.Instance.CurrentCulture);
            _dialogs.Info(Text("CompaniesExported"));
        }
        catch (Exception ex)
        {
            _dialogs.Error(ex.Message);
        }
    }

    private async Task ReloadItemsAsync()
    {
        var filter = new CompanyFilter
        {
            SearchText = SearchText,
            IncludeArchived = IncludeArchived,
            AssignedEmployeeId = (SelectedEmployeeId.HasValue && SelectedEmployeeId.Value != Guid.Empty) ? SelectedEmployeeId.Value : null
        };

        var list = await _companies.GetCompaniesAsync(filter);
        Items.Clear();
        foreach (var item in list)
        {
            Items.Add(item);
        }

        Count = Items.Count;
        Status = string.Format(CultureInfo.CurrentCulture,
            Text(IncludeArchived ? "CompaniesRecordCount" : "CompaniesActiveCount"), Count);

        if (Employees.Count == 0)
        {
            await LoadEmployeesAsync();
        }
    }

    private void OnCultureChanged()
    {
        if (Employees.FirstOrDefault(e => e.Id == Guid.Empty) is { } allEmployees)
            allEmployees.FullName = Text("CompaniesAllEmployees");

        Status = Count == 0
            ? Text("CommonReadyStatus")
            : string.Format(CultureInfo.CurrentCulture,
                Text(IncludeArchived ? "CompaniesRecordCount" : "CompaniesActiveCount"), Count);
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
