using System.Globalization;
using System.Collections.ObjectModel;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly ICompanyFolderService _folderService;
    private readonly ICompanyExcelExporter _excelExporter;
    private readonly WindowsDialogService _dialogs;
    private readonly WindowsFolderPicker _folderPicker;

    public MainWindowViewModel(
        IServiceProvider serviceProvider,
        ICompanyService companyService,
        ISettingsService settingsService,
        ICompanyFolderService folderService,
        ICompanyExcelExporter excelExporter,
        WindowsDialogService dialogs,
        WindowsFolderPicker folderPicker)
    {
        _serviceProvider = serviceProvider;
        _companyService = companyService;
        _settingsService = settingsService;
        _folderService = folderService;
        _excelExporter = excelExporter;
        _dialogs = dialogs;
        _folderPicker = folderPicker;

        _userName = Environment.UserName;
        UpdateCultureSensitiveText();
        LocalizationService.Instance.CultureChanged += UpdateCultureSensitiveText;
    }

    public async Task InitializeAsync()
    {
        _userName = await _settingsService.GetAsync(AlphaLedger.Core.SettingKeys.CurrentUserName);
        _userRole = await _settingsService.GetAsync(AlphaLedger.Core.SettingKeys.CurrentUserRole);
        if (string.IsNullOrWhiteSpace(_userName))
        {
            _userName = Environment.UserName;
        }
        UpdateCultureSensitiveText();
    }

    private string? _activeScreenKey;
    private string? _activeCompanyName;
    private string? _userName;
    private string? _userRole;
    [ObservableProperty] private WorkspaceTabItem? _selectedTab;

    [ObservableProperty] private object? _activeScreen;
    [ObservableProperty] private string _activeScreenTitle = string.Empty;
    [ObservableProperty] private string _currentNav = "dashboard";
    [ObservableProperty] private string _databaseStatus = string.Empty;
    [ObservableProperty] private string _currentUser = string.Empty;
    [ObservableProperty] private string _currentUserRole = string.Empty;
    [ObservableProperty] private string _currentDate = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _quickSearchText = string.Empty;

    public ObservableCollection<WorkspaceTabItem> OpenTabs { get; } = [];

    public void ShowScreen(string key, string title, object screen, string? companyName = null)
    {
        var tab = OpenTabs.FirstOrDefault(item => item.Key == key);
        if (tab is null)
        {
            tab = new WorkspaceTabItem(key, title, screen, companyName);
            OpenTabs.Add(tab);
        }
        else
        {
            tab.Title = title;
            tab.Screen = screen;
            tab.CompanyName = companyName;
        }
        SelectedTab = tab;
        _activeScreenKey = key;
        _activeCompanyName = companyName;
        CurrentNav = key;
        ActiveScreenTitle = title;
        ActiveScreen = screen;
    }

    [RelayCommand]
    private void SelectTab(WorkspaceTabItem? tab)
    {
        if (tab is not null) SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(WorkspaceTabItem? tab)
    {
        if (tab is null || !OpenTabs.Contains(tab)) return;
        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        if (SelectedTab == tab)
        {
            SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(index, OpenTabs.Count - 1)];
            if (SelectedTab is null)
            {
                ActiveScreen = null;
                _activeScreenKey = null;
                _activeCompanyName = null;
                ActiveScreenTitle = string.Empty;
            }
        }
    }

    [RelayCommand]
    private void CloseAllTabs()
    {
        OpenTabs.Clear();
        SelectedTab = null;
        ActiveScreen = null;
        ActiveScreenTitle = string.Empty;
        CurrentNav = string.Empty;
        _activeScreenKey = null;
        _activeCompanyName = null;
    }

    partial void OnSelectedTabChanged(WorkspaceTabItem? value)
    {
        if (value is null) return;
        foreach (var tab in OpenTabs) tab.IsSelected = tab == value;
        _activeScreenKey = value.Key;
        _activeCompanyName = value.CompanyName;
        CurrentNav = value.Key;
        ActiveScreen = value.Screen;
        ActiveScreenTitle = value.Title;
    }

    [RelayCommand]
    public void QuickSearch()
    {
        if (string.IsNullOrWhiteSpace(QuickSearchText))
        {
            return;
        }

        // Switch to companies tab with the search filter
        CurrentNav = "companies";
        // The view/controller can apply search
    }

    private void UpdateCultureSensitiveText()
    {
        DatabaseStatus = GetString("DatabaseStatus");
        CurrentUser = string.IsNullOrWhiteSpace(_userName) ? GetString("CurrentUser") : _userName;
        CurrentUserRole = string.IsNullOrWhiteSpace(_userRole) ? GetString("CurrentUserRole") : _userRole;
        var applicationVersion = typeof(MainWindowViewModel).Assembly.GetName().Version;
        var versionText = applicationVersion?.ToString(3) ?? "1.0.0";
        Version = string.Format(CultureInfo.CurrentCulture, GetString("Version"), versionText);
        CurrentDate = DateTime.Now.ToString("D", LocalizationService.Instance.CurrentCulture);

        var titleKey = _activeScreenKey switch
        {
            "dashboard" => "NavDashboard",
            "companies" => "NavCompanies",
            "fiscalyears" => "FiscalYearsTitle",
            "operations" => "OperationsTitle",
            "taxreturns" => "TaxReturnsTitle",
            "financials" => "NavFinancials",
            "archive" => "NavArchive",
            "staff" => "NavStaff",
            "reports" => "NavReports",
            "settings" => "SettingsTitle",
            _ => null
        };

        if (titleKey is not null)
            ActiveScreenTitle = GetString(titleKey);
        else if (_activeCompanyName is not null)
            ActiveScreenTitle = string.Format(CultureInfo.CurrentCulture, GetString("CompanyProfileTabTitle"), _activeCompanyName);

        foreach (var tab in OpenTabs)
        {
            var key = tab.Key switch
            {
                "dashboard" => "NavDashboard", "companies" => "NavCompanies", "fiscalyears" => "FiscalYearsTitle",
                "operations" => "OperationsTitle", "taxreturns" => "TaxReturnsTitle", "financials" => "NavFinancials",
                "archive" => "NavArchive", "staff" => "NavStaff", "reports" => "NavReports", "settings" => "SettingsTitle",
                _ => null
            };
            if (key is not null) tab.Title = GetString(key);
            else if (tab.CompanyName is not null)
                tab.Title = string.Format(CultureInfo.CurrentCulture, GetString("CompanyProfileTabTitle"), tab.CompanyName);
        }
    }

    private static string GetString(string key) => LocalizationService.Instance.GetString(key);
}

public partial class WorkspaceTabItem(string key, string title, object screen, string? companyName) : ObservableObject
{
    public string Key { get; } = key;
    public string? CompanyName { get; set; } = companyName;
    [ObservableProperty] private string _title = title;
    [ObservableProperty] private object _screen = screen;
    [ObservableProperty] private bool _isSelected;
}
