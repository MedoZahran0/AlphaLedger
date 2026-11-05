using System.Collections.ObjectModel;
using System.Globalization;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ICompanyService _companies;
    private readonly Action<Guid, string>? _openCompanyProfile;
    private readonly Action? _navigateToCompanies;

    public DashboardViewModel(
        ICompanyService companies,
        Action<Guid, string>? openCompanyProfile = null,
        Action? navigateToCompanies = null)
    {
        _companies = companies;
        _openCompanyProfile = openCompanyProfile;
        _navigateToCompanies = navigateToCompanies;
        StatusMessage = Text("CommonReadyStatus");
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    [ObservableProperty] private int _activeCompanies;
    [ObservableProperty] private int _archivedCompanies;
    [ObservableProperty] private decimal _totalFees;
    [ObservableProperty] private decimal _totalCollections;
    [ObservableProperty] private decimal _outstandingFees;
    [ObservableProperty] private int _overdueOperations;
    [ObservableProperty] private int _upcomingDeadlines;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<CompanyListItem> RecentCompanies { get; } = [];
    public ObservableCollection<CompanyListItem> OutstandingCompanies { get; } = [];
    public ObservableCollection<CompanyListItem> UpcomingDeadlinesList { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var all = await _companies.GetCompaniesAsync(new CompanyFilter { IncludeArchived = true });
            var active = all.Where(c => !c.IsArchived).ToList();

            ActiveCompanies = active.Count;
            ArchivedCompanies = all.Count - active.Count;
            TotalFees = active.Sum(c => c.TotalAgreedFees);
            TotalCollections = active.Sum(c => c.TotalCollections);
            OutstandingFees = active.Sum(c => c.RemainingFees);
            OverdueOperations = active.Sum(c => c.OutstandingOperations);
            UpcomingDeadlines = active.Count(c => c.NextTaxDeadline.HasValue);

            RecentCompanies.Clear();
            foreach (var item in active.Take(6))
            {
                RecentCompanies.Add(item);
            }

            OutstandingCompanies.Clear();
            foreach (var item in active.Where(c => c.RemainingFees > 0).OrderByDescending(c => c.RemainingFees).Take(6))
            {
                OutstandingCompanies.Add(item);
            }

            UpcomingDeadlinesList.Clear();
            foreach (var item in active.Where(c => c.NextTaxDeadline.HasValue).OrderBy(c => c.NextTaxDeadline).Take(6))
            {
                UpcomingDeadlinesList.Add(item);
            }

            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("DashboardLastUpdate"), DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("DashboardErrorLoading"), LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public void OpenCompany(CompanyListItem? item)
    {
        if (item != null)
        {
            _openCompanyProfile?.Invoke(item.Id, item.DisplayName);
        }
    }

    [RelayCommand]
    public void GoToCompanies()
    {
        _navigateToCompanies?.Invoke();
    }

    private void OnCultureChanged()
    {
        if (StatusMessage.Length == 0 || StatusMessage == Text("CommonReadyStatus"))
            StatusMessage = Text("CommonReadyStatus");
        else
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("DashboardLastUpdate"), DateTime.Now);
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
