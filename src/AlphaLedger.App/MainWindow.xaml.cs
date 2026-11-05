using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AlphaLedger.App.Localization;
using AlphaLedger.App.ViewModels;
using AlphaLedger.App.Views;
using AlphaLedger.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    public MainWindowViewModel ViewModel => _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, FrameworkElement> _screenCache = new(StringComparer.Ordinal);
    private CompaniesViewModel? _companiesViewModel;
    private DashboardViewModel? _dashboardViewModel;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        ShowScreen("dashboard", LocalizationService.Instance.GetString("NavDashboard"), NavDashboard, GetOrCreateDashboardView);
    }

    private DashboardView GetOrCreateDashboardView()
    {
        var view = new DashboardView();
        _dashboardViewModel = CreateViewModel<DashboardViewModel>(
            new Action<Guid, string>((id, name) => OpenCompanyProfile(id, name)),
            new Action(() => OpenCompaniesScreen()));
        view.DataContext = _dashboardViewModel;
        return view;
    }

    private CompaniesView GetOrCreateCompaniesView(string? initialSearch = null)
    {
        var view = new CompaniesView();
        _companiesViewModel = CreateViewModel<CompaniesViewModel>(
            new Action<Guid, string>((id, name) => OpenCompanyProfile(id, name)),
            new Action<Guid>(id => OpenArchiveScreen(id)));

        if (!string.IsNullOrWhiteSpace(initialSearch))
        {
            _companiesViewModel.SearchText = initialSearch;
        }

        _companiesViewModel.RequestAdd += () =>
        {
            var editVm = CreateViewModel<CompanyEditViewModel>();
            var dialog = new CompanyEditDialog(editVm) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _ = _companiesViewModel.LoadAsync();
                if (_dashboardViewModel != null)
                {
                    _ = _dashboardViewModel.LoadAsync();
                }
            }
        };

        _companiesViewModel.RequestEdit += item =>
        {
            var editVm = CreateViewModel<CompanyEditViewModel>(item.Id);
            var dialog = new CompanyEditDialog(editVm) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _ = _companiesViewModel.LoadAsync();
                if (_dashboardViewModel != null)
                {
                    _ = _dashboardViewModel.LoadAsync();
                }
            }
        };

        view.DataContext = _companiesViewModel;
        return view;
    }

    public void OpenCompaniesScreen(string? search = null)
    {
        ShowScreen("companies", LocalizationService.Instance.GetString("CompaniesTitle"), NavCompanies,
            () => GetOrCreateCompaniesView(search));

        if (!string.IsNullOrWhiteSpace(search) && _companiesViewModel != null)
        {
            _companiesViewModel.SearchText = search;
            _ = _companiesViewModel.LoadAsync();
        }
    }

    private static readonly Dictionary<string, Window> _portalWindows = new();

    public void OpenPortalWindow(string url, string portalName, string username, string password, string companyName)
    {
        var key = $"{companyName}|{portalName}|{url}";
        if (_portalWindows.TryGetValue(key, out var existing) && existing.IsVisible)
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var view = new Views.PortalBrowserView
        {
            PortalUrl = url,
            PortalName = portalName,
            PortalUsername = username,
            PortalPassword = password,
            CompanyName = companyName
        };

        var area = SystemParameters.WorkArea;
        var window = new Window
        {
            Title = string.IsNullOrWhiteSpace(portalName) ? "Alpha Ledger" : $"{companyName} - {portalName}",
            Content = view,
            Width = area.Width * 0.9,
            Height = area.Height * 0.9,
            MinWidth = 900,
            MinHeight = 600,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowState = WindowState.Maximized,
            ResizeMode = ResizeMode.CanResize
        };

        window.Closed += (s, e) => _portalWindows.Remove(key);
        _portalWindows[key] = window;
        window.Show();
    }
    public void OpenCompanyProfile(Guid companyId, string companyName)
    {
        var screenKey = $"company_{companyId}";
        ShowScreen(screenKey, string.Format(LocalizationService.Instance.CurrentCulture,
            LocalizationService.Instance.GetString("CompanyProfileTabTitle"), companyName), NavCompanies, () =>
        {
            var view = new CompanyProfileView();
            var vm = CreateViewModel<CompanyProfileViewModel>(companyId);
            vm.OpenFinancialsRequested += (id, yearId) => OpenFinancialsScreen(id, fiscalYearId: yearId);
            vm.AddFeeRequested += (id, yearId) => OpenFinancialsScreen(id, startNewFee: true, fiscalYearId: yearId);
            vm.OpenFiscalYearsRequested += companyId => OpenFiscalYearsScreen(companyId);
            vm.OpenArchiveRequested += companyId => OpenArchiveScreen(companyId);
            vm.EditCompanyRequested += () =>
            {
                var editViewModel = CreateViewModel<CompanyEditViewModel>(companyId);
                var dialog = new CompanyEditDialog(editViewModel) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    _ = vm.LoadAsync();
                    if (_dashboardViewModel is not null) _ = _dashboardViewModel.LoadAsync();
                    if (_companiesViewModel is not null) _ = _companiesViewModel.LoadAsync();
                }
            };
            vm.OpenOperationsRequested += (id, yearId) => OpenOperationsScreen(id, yearId);
            vm.OpenTaxReturnsRequested += (id, yearId) => OpenTaxReturnsScreen(id, yearId);
            vm.OpenPortalRequested += (url, name, username, password, companyName) => OpenPortalWindow(url, name, username, password, companyName);
            view.DataContext = vm;
            return view;
        }, companyName);
    }

    public void OpenFinancialsScreen(Guid? companyId = null, bool startNewFee = false, Guid? fiscalYearId = null)
    {
        if (_screenCache.TryGetValue("financials", out var cached) && cached.DataContext is FeesViewModel existingViewModel)
        {
            existingViewModel.SelectedCompanyId = companyId;
            existingViewModel.SelectedFiscalYearId = fiscalYearId;
            _viewModel.ShowScreen("financials", LocalizationService.Instance.GetString("NavFinancials"), cached,
                _companiesViewModel?.Items.FirstOrDefault(item => item.Id == companyId)?.DisplayName);
            NavFinancials.IsChecked = true;
            _ = existingViewModel.LoadAsync();
            if (startNewFee) existingViewModel.NewFee();
            return;
        }

        ShowScreen("financials", LocalizationService.Instance.GetString("NavFinancials"), NavFinancials, () =>
        {
            var view = new FeesView();
            var feesViewModel = CreateViewModel<FeesViewModel>(companyId ?? Guid.Empty, fiscalYearId ?? Guid.Empty);
            feesViewModel.OpenArchiveRequested += id => OpenArchiveScreen(id);
            feesViewModel.DataChanged += () => RefreshCompanyProfile(feesViewModel.SelectedCompanyId);
            if (startNewFee) feesViewModel.NewFee();
            view.DataContext = feesViewModel;
            return view;
        });
    }

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen("dashboard", LocalizationService.Instance.GetString("NavDashboard"), NavDashboard, GetOrCreateDashboardView);
    }

    private void NavCompanies_Click(object sender, RoutedEventArgs e)
    {
        OpenCompaniesScreen();
    }

    private void NavFiscalYears_Click(object sender, RoutedEventArgs e)
    {
        OpenFiscalYearsScreen();
    }

    private void OpenFiscalYearsScreen(Guid? companyId = null)
    {
        if (_screenCache.TryGetValue("fiscalyears", out var cached) && cached.DataContext is FiscalYearsViewModel existingViewModel)
        {
            if (companyId.HasValue) existingViewModel.SelectedCompanyId = companyId;
            ShowScreen("fiscalyears", LocalizationService.Instance.GetString("FiscalYearsTitle"), NavFiscalYears, () => cached);
            _ = existingViewModel.InitializeAsync();
            return;
        }
        ShowScreen("fiscalyears", LocalizationService.Instance.GetString("FiscalYearsTitle"), NavFiscalYears, () =>
        {
            var fiscalYearsViewModel = CreateViewModel<FiscalYearsViewModel>(companyId ?? Guid.Empty);
            fiscalYearsViewModel.DataChanged += () => RefreshCompanyProfile(fiscalYearsViewModel.SelectedCompanyId);
            var view = new FiscalYearsView(fiscalYearsViewModel);
            return view;
        });
    }

    private void NavOperations_Click(object sender, RoutedEventArgs e)
    {
        OpenOperationsScreen();
    }

    private void OpenOperationsScreen(Guid? companyId = null, Guid? fiscalYearId = null)
    {
        if (_screenCache.TryGetValue("operations", out var cached) && cached.DataContext is OfficeOperationsViewModel existingViewModel)
        {
            existingViewModel.SelectedCompanyId = companyId;
            existingViewModel.SelectedFiscalYearId = fiscalYearId;
            ShowScreen("operations", LocalizationService.Instance.GetString("OperationsTitle"), NavOperations, () => cached);
            _ = existingViewModel.InitializeAsync();
            return;
        }

        ShowScreen("operations", LocalizationService.Instance.GetString("OperationsTitle"), NavOperations, () =>
        {
            var viewModel = CreateViewModel<OfficeOperationsViewModel>(companyId ?? Guid.Empty, fiscalYearId ?? Guid.Empty);
            viewModel.OpenCompanyRequested += id => OpenCompanyProfile(
                id, viewModel.Companies.FirstOrDefault(company => company.Id == id)?.DisplayName ?? string.Empty);
            viewModel.OpenArchiveRequested += id => OpenArchiveScreen(id);
            viewModel.DataChanged += () => RefreshCompanyProfile(viewModel.SelectedCompanyId);
            var view = new OfficeOperationsView(viewModel);
            return view;
        }, companyName: null);
    }

    private void NavTaxReturns_Click(object sender, RoutedEventArgs e)
    {
        OpenTaxReturnsScreen();
    }

    private void OpenTaxReturnsScreen(Guid? companyId = null, Guid? fiscalYearId = null)
    {
        if (_screenCache.TryGetValue("taxreturns", out var cached) && cached.DataContext is TaxReturnsViewModel existingViewModel)
        {
            existingViewModel.SelectedCompanyId = companyId;
            existingViewModel.SelectedFiscalYearId = fiscalYearId;
            ShowScreen("taxreturns", LocalizationService.Instance.GetString("TaxReturnsTitle"), NavTaxReturns, () => cached);
            _ = existingViewModel.InitializeAsync();
            return;
        }

        ShowScreen("taxreturns", LocalizationService.Instance.GetString("TaxReturnsTitle"), NavTaxReturns, () =>
        {
            var viewModel = CreateViewModel<TaxReturnsViewModel>(companyId ?? Guid.Empty, fiscalYearId ?? Guid.Empty);
            viewModel.OpenCompanyRequested += id => OpenCompanyProfile(
                id, viewModel.Companies.FirstOrDefault(company => company.Id == id)?.DisplayName ?? string.Empty);
            viewModel.OpenArchiveRequested += id => OpenArchiveScreen(id);
            viewModel.DataChanged += () => RefreshCompanyProfile(viewModel.SelectedCompanyId);
            var view = new TaxReturnsView(viewModel);
            return view;
        }, companyName: null);
    }

    private void NavFinancials_Click(object sender, RoutedEventArgs e)
    {
        OpenFinancialsScreen();
    }

    private void NavArchive_Click(object sender, RoutedEventArgs e)
    {
        OpenArchiveScreen();
    }

    public void OpenArchiveScreen(Guid? companyId = null)
    {
        try
        {
            ShowScreen("archive", LocalizationService.Instance.GetString("NavArchive"), NavArchive, GetOrCreateArchiveView);
            if (companyId is Guid id && _screenCache.TryGetValue("archive", out var screen) && screen.DataContext is ArchiveViewModel archiveViewModel)
                _ = archiveViewModel.SelectCompanyByIdAsync(id);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Archive Error");
        }
    }

    private void RefreshCompanyProfile(Guid? companyId)
    {
        if (companyId is not Guid id || !_screenCache.TryGetValue($"company_{id}", out var screen) ||
            screen.DataContext is not CompanyProfileViewModel profile)
            return;

        _ = profile.LoadAsync();
    }

    private ArchiveView GetOrCreateArchiveView()
    {
        var view = new ArchiveView();
        var vm = CreateViewModel<ArchiveViewModel>();
        view.DataContext = vm;
        _ = vm.LoadAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    System.Windows.MessageBox.Show(t.Exception?.GetBaseException().Message ?? "Archive load failed", "Archive Error"));
            }
        });
        return view;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.BrowserBack) || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox textBox && !string.IsNullOrEmpty(textBox.Text)) return;
        if (_viewModel.ActiveScreen is ArchiveView { DataContext: ArchiveViewModel archiveViewModel } && archiveViewModel.CanNavigateBack)
        {
            e.Handled = true;
            archiveViewModel.NavigateBack();
        }
    }

    private void NavStaff_Click(object sender, RoutedEventArgs e) => OpenStaffScreen();

    public void OpenStaffScreen()
    {
        ShowScreen("staff", LocalizationService.Instance.GetString("StaffTitle"), NavStaff, () =>
        {
            var view = new StaffView();
            var vm = CreateViewModel<StaffViewModel>();
            _ = vm.LoadAsync();
            view.DataContext = vm;
            return view;
        });
    }

    private void NavReports_Click(object sender, RoutedEventArgs e) => OpenReportsScreen();

    public void OpenReportsScreen()
    {
        ShowScreen("reports", LocalizationService.Instance.GetString("ReportsTitle"), NavReports, () =>
        {
            var view = new ReportsView();
            var vm = CreateViewModel<ReportsViewModel>();
            _ = vm.LoadAsync();
            view.DataContext = vm;
            return view;
        });
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsScreen();
    }

    public void OpenSettingsScreen()
    {
        ShowScreen("settings", LocalizationService.Instance.GetString("SettingsTitle"), NavSettings, () =>
        {
            var view = new SettingsView();
            var vm = CreateViewModel<SettingsViewModel>();
            vm.IdentityChanged += () => _ = _viewModel.InitializeAsync();
            _ = vm.LoadAsync();
            view.DataContext = vm;
            return view;
        });
    }

    private void TopSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenCompaniesScreen(TopSearchBox.Text);
        }
    }

    private void RefreshCurrentScreen_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ActiveScreen is FrameworkElement fe)
        {
            if (fe.DataContext is DashboardViewModel dashVm)
            {
                _ = dashVm.LoadAsync();
            }
            else if (fe.DataContext is CompaniesViewModel compVm)
            {
                _ = compVm.LoadAsync();
            }
            else if (fe.DataContext is SettingsViewModel setVm)
            {
                _ = setVm.LoadAsync();
            }
            else if (fe.DataContext is CompanyProfileViewModel profVm)
            {
                _ = profVm.LoadAsync();
            }
            else if (fe.DataContext is FiscalYearsViewModel fyVm)
            {
                _ = fyVm.InitializeAsync();
            }
            else if (fe.DataContext is OfficeOperationsViewModel operationsVm)
            {
                _ = operationsVm.InitializeAsync();
            }
            else if (fe.DataContext is TaxReturnsViewModel taxReturnsVm)
            {
                _ = taxReturnsVm.InitializeAsync();
            }
            else if (fe.DataContext is StaffViewModel staffVm)
            {
                _ = staffVm.LoadAsync();
            }
            else if (fe.DataContext is ReportsViewModel reportsVm)
            {
                _ = reportsVm.LoadAsync();
            }
        }
    }

    private T CreateViewModel<T>(params object[] parameters) where T : notnull
        => ActivatorUtilities.CreateInstance<T>(_serviceProvider, parameters);

    private void ShowScreen(string key, string title, RadioButton? navigationItem, Func<FrameworkElement> screenFactory, string? companyName = null)
    {
        if (!_screenCache.TryGetValue(key, out var screen))
        {
            screen = screenFactory();
            _screenCache.Add(key, screen);
        }

        _viewModel.ShowScreen(key, title, screen, companyName);
        if (navigationItem is not null)
            navigationItem.IsChecked = true;
    }
}
