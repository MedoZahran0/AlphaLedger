using System.Collections.ObjectModel;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

/// <summary>Accounting reports screen: company-level fees and collections summary.</summary>
public partial class ReportsViewModel : ObservableObject
{
    private readonly ICompanyService _companies;

    public ReportsViewModel(ICompanyService companies)
    {
        _companies = companies;
    }

    public ObservableCollection<CompanyListItem> Items { get; } = [];

    [ObservableProperty] private decimal _totalFees;
    [ObservableProperty] private decimal _totalCollections;
    [ObservableProperty] private decimal _outstandingFees;
    [ObservableProperty] private int _activeCompanies;
    [ObservableProperty] private bool _includeArchived;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    partial void OnIncludeArchivedChanged(bool value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var companies = await _companies.GetCompaniesAsync(new CompanyFilter { IncludeArchived = IncludeArchived });
            Items.Clear();
            foreach (var company in companies)
            {
                Items.Add(company);
            }

            TotalFees = Items.Sum(item => item.TotalAgreedFees);
            TotalCollections = Items.Sum(item => item.TotalCollections);
            OutstandingFees = Items.Sum(item => item.RemainingFees);
            ActiveCompanies = Items.Count(item => !item.IsArchived);
            StatusMessage = Items.Count == 0 ? Text("ReportsNoData") : string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
