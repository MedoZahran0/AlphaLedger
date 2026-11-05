using System.Collections.ObjectModel;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace AlphaLedger.App.ViewModels;

public partial class CompanyProfileViewModel : ObservableObject
{
    private readonly ICompanyService _companyService;
    private readonly ICompanyFolderService _folderService;
    private readonly ICompanyArchiveService _archiveService;
    private readonly IOperationService _operationService;
    private readonly ITaxReturnService _taxReturnService;
    private readonly IFeeService _feeService;
    private readonly ICollectionService _collectionService;
    private readonly ICompanyPortalAccountService _portalAccountService;
    private readonly IWindowsDataProtector _dataProtector;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid _companyId;

    public CompanyProfileViewModel(
        ICompanyService companyService,
        ICompanyFolderService folderService,
        ICompanyArchiveService archiveService,
        IOperationService operationService,
        ITaxReturnService taxReturnService,
        IFeeService feeService,
        ICollectionService collectionService,
        ICompanyPortalAccountService portalAccountService,
        IWindowsDataProtector dataProtector,
        WindowsDialogService dialogs,
        Guid companyId)
    {
        _companyService = companyService;
        _folderService = folderService;
        _archiveService = archiveService;
        _operationService = operationService;
        _taxReturnService = taxReturnService;
        _feeService = feeService;
        _collectionService = collectionService;
        _portalAccountService = portalAccountService;
        _dataProtector = dataProtector;
        _dialogs = dialogs;
        _companyId = companyId;
        LocalizationService.Instance.CultureChanged += OnCultureChanged;
    }

    [ObservableProperty] private Company? _company;
    [ObservableProperty] private CompanyFiscalYear? _selectedFiscalYear;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _folderExists;
    [ObservableProperty] private string? _folderWarning;
    [ObservableProperty] private decimal _totalAgreedFees;
    [ObservableProperty] private decimal _totalCollections;
    [ObservableProperty] private decimal _outstandingBalance;
    [ObservableProperty] private CompanyPortalAccount? _selectedPortalAccount;
    [ObservableProperty] private string _portalName = string.Empty;
    [ObservableProperty] private string _portalUrl = string.Empty;
    [ObservableProperty] private string _portalUsername = string.Empty;
    [ObservableProperty] private string _portalPassword = string.Empty;
    [ObservableProperty] private string? _portalStatus;
    [ObservableProperty] private bool _isPortalVisible;
    [ObservableProperty] private string _portalSavedUsername = string.Empty;
    [ObservableProperty] private string _portalSavedPassword = string.Empty;

    public ObservableCollection<CompanyFiscalYear> FiscalYears { get; } = [];
    public ObservableCollection<OfficeOperation> Operations { get; } = [];
    public ObservableCollection<TaxReturn> TaxReturns { get; } = [];
    public ObservableCollection<Fee> Fees { get; } = [];
    public ObservableCollection<Collection> Collections { get; } = [];
    public ObservableCollection<CompanyPortalAccount> PortalAccounts { get; } = [];
    public event Action<Guid, Guid?>? OpenFinancialsRequested;
    public event Action<Guid, Guid?>? OpenOperationsRequested;
    public event Action<Guid, Guid?>? OpenTaxReturnsRequested;
    public event Action<Guid>? OpenArchiveRequested;
    public event Action<Guid, Guid?>? AddFeeRequested;
    public event Action<Guid>? OpenFiscalYearsRequested;
    public event Action? EditCompanyRequested;

    [RelayCommand]
    public void EditCompany() => EditCompanyRequested?.Invoke();

    [RelayCommand]
    public void AddFee()
    {
        if (Company is not null)
            AddFeeRequested?.Invoke(Company.Id, SelectedFiscalYear?.Id);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Company = await _companyService.GetCompanyAsync(_companyId);
            await LoadCompanyActivityAsync();
            await LoadPortalAccountsAsync();
            if (Company != null)
            {
                Company.DocumentStoragePath = await _archiveService.GetCompanyArchiveRootAsync(Company.Id, Company.Code);
                var check = _folderService.CheckFolder(Company.DocumentStoragePath);
                FolderExists = check.Exists;
                FolderWarning = check.Warning;

                FiscalYears.Clear();
                foreach (var year in Company.FiscalYears.OrderByDescending(y => y.StartDate))
                {
                    FiscalYears.Add(year);
                }
                var today = DateOnly.FromDateTime(DateTime.Today);
                SelectedFiscalYear = FiscalYears.FirstOrDefault(year => year.Status == FiscalYearStatus.Active && year.StartDate <= today && year.EndDate >= today)
                    ?? FiscalYears.FirstOrDefault(year => year.Status == FiscalYearStatus.Active)
                    ?? FiscalYears.FirstOrDefault();
            }
            StatusMessage = Text("CompanyProfileLoaded");
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPortalAccountsAsync(Guid? preferredAccountId = null)
    {
        var accounts = await _portalAccountService.GetForCompanyAsync(_companyId);
        PortalAccounts.Clear();
        foreach (var account in accounts)
            PortalAccounts.Add(account);

        var accountId = preferredAccountId ?? SelectedPortalAccount?.Id;
        if (accountId.HasValue)
            SelectedPortalAccount = PortalAccounts.FirstOrDefault(account => account.Id == accountId.Value);
        else if (PortalAccounts.Count > 0)
            SelectedPortalAccount = PortalAccounts[0];
        else
            SelectedPortalAccount = null;
    }

    [RelayCommand]
    public void AddPortalAccount()
    {
        SelectedPortalAccount = null;
        PortalName = string.Empty;
        PortalUrl = string.Empty;
        PortalUsername = string.Empty;
        PortalPassword = string.Empty;
        PortalStatus = null;
    }

    [RelayCommand]
    public async Task SavePortalAccountAsync()
    {
        if (Company is null)
            return;

        try
        {
            var account = new CompanyPortalAccount
            {
                Id = SelectedPortalAccount?.Id ?? Guid.NewGuid(),
                CompanyId = Company.Id,
                PortalName = PortalName.Trim(),
                Url = PortalUrl.Trim(),
                Username = PortalUsername.Trim()
            };

            CompanyPortalAccount savedAccount;
            if (SelectedPortalAccount is null)
                savedAccount = await _portalAccountService.AddAsync(account, PortalPassword);
            else
                savedAccount = await _portalAccountService.UpdateAsync(account, PortalPassword);

            PortalStatus = Text("PortalAccountSaved");
            await LoadPortalAccountsAsync(savedAccount.Id);
        }
        catch (Exception ex)
        {
            PortalStatus = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
    }

    [RelayCommand]
    public async Task DeletePortalAccountAsync()
    {
        if (SelectedPortalAccount is null)
            return;

        if (!_dialogs.Confirm(Text("PortalAccountDeleteConfirm")))
            return;

        try
        {
            await _portalAccountService.DeleteAsync(SelectedPortalAccount.Id);
            AddPortalAccount();
            await LoadPortalAccountsAsync();
        }
        catch (Exception ex)
        {
            PortalStatus = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
    }

    [RelayCommand]
    public void CopyPortalUsername()
    {
        if (SelectedPortalAccount is null || string.IsNullOrWhiteSpace(PortalUsername))
            return;

        CopyPortalCredential(PortalUsername, "PortalUsernameCopied");
    }

    [RelayCommand]
    public void CopyPortalPassword()
    {
        if (SelectedPortalAccount is null || string.IsNullOrWhiteSpace(PortalPassword))
            return;

        CopyPortalCredential(PortalPassword, "PortalPasswordCopied");
    }

    private void CopyPortalCredential(string value, string successMessage)
    {
        try
        {
            Clipboard.SetText(value);
            PortalStatus = Text(successMessage);
        }
        catch (Exception ex)
        {
            PortalStatus = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
    }

    public event Action<string, string, string, string, string>? OpenPortalRequested;

    [RelayCommand]
    public void OpenPortal()
    {
        if (SelectedPortalAccount is null)
        {
            PortalStatus = Text("PortalSelectAccount");
            return;
        }

        PortalSavedUsername = SelectedPortalAccount.Username ?? string.Empty;
        if (SelectedPortalAccount.ProtectedPassword is { Length: > 0 })
        {
            try
            {
                PortalSavedPassword = System.Text.Encoding.UTF8.GetString(_dataProtector.Unprotect(SelectedPortalAccount.ProtectedPassword));
            }
            catch
            {
                PortalSavedPassword = string.Empty;
            }
        }
        else
        {
            PortalSavedPassword = string.Empty;
        }

        OpenPortalRequested?.Invoke(
            SelectedPortalAccount.Url ?? string.Empty,
            SelectedPortalAccount.PortalName ?? string.Empty,
            PortalSavedUsername,
            PortalSavedPassword,
            Company?.DisplayName ?? string.Empty);

        PortalStatus = Text("PortalManualSignIn");
    }

    partial void OnSelectedPortalAccountChanged(CompanyPortalAccount? value)
    {
        if (value is null)
        {
            PortalPassword = string.Empty;
            return;
        }

        PortalName = value.PortalName;
        PortalUrl = value.Url;
        PortalUsername = value.Username;
        PortalPassword = UnprotectPortalPassword(value);
    }

    private string UnprotectPortalPassword(CompanyPortalAccount account)
    {
        if (account.ProtectedPassword is not { Length: > 0 }) return string.Empty;
        byte[]? passwordBytes = null;
        try
        {
            passwordBytes = _dataProtector.Unprotect(account.ProtectedPassword);
            return System.Text.Encoding.UTF8.GetString(passwordBytes);
        }
        catch
        {
            PortalStatus = Text("PortalPasswordCouldNotLoad");
            return string.Empty;
        }
        finally
        {
            if (passwordBytes is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private async Task LoadCompanyActivityAsync()
    {
        var operations = await _operationService.GetOperationsAsync(_companyId);
        Operations.Clear();
        foreach (var operation in operations.OrderByDescending(item => item.CreatedAtUtc))
            Operations.Add(operation);

        var taxReturns = await _taxReturnService.GetTaxReturnsAsync(_companyId);
        TaxReturns.Clear();
        foreach (var taxReturn in taxReturns.OrderBy(item => item.FilingDueDate))
            TaxReturns.Add(taxReturn);

        var fees = await _feeService.GetFeesAsync(_companyId);
        Fees.Clear();
        foreach (var fee in fees.OrderByDescending(item => item.FeeDate))
            Fees.Add(fee);

        var collections = await _collectionService.GetCollectionsAsync(_companyId);
        Collections.Clear();
        foreach (var collection in collections.OrderByDescending(item => item.CollectionDate))
            Collections.Add(collection);

        TotalAgreedFees = fees.Where(fee => fee.Status != FeeStatus.Cancelled).Sum(fee => fee.Amount);
        TotalCollections = collections.Where(collection => !collection.IsReversed).Sum(collection => collection.AmountReceived);
        OutstandingBalance = TotalAgreedFees - TotalCollections;
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (Company is not null)
            OpenArchiveRequested?.Invoke(Company.Id);
    }

    [RelayCommand]
    public void OpenFolderOnDisk()
    {
        if (Company is null) return;

        try
        {
            ShellService.OpenFolder(Company.DocumentStoragePath);
            StatusMessage = string.Format(Text("ArchiveOpenedOnDisk"), Company.DisplayName);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
    }

    [RelayCommand]
    public void OpenFinancials()
    {
        if (Company is not null)
            OpenFinancialsRequested?.Invoke(Company.Id, SelectedFiscalYear?.Id);
    }

    [RelayCommand]
    public void OpenFiscalYears()
    {
        if (Company is not null) OpenFiscalYearsRequested?.Invoke(Company.Id);
    }

    [RelayCommand]
    public void OpenOperations()
    {
        if (Company is not null)
            OpenOperationsRequested?.Invoke(Company.Id, SelectedFiscalYear?.Id);
    }

    [RelayCommand]
    public void OpenTaxReturns()
    {
        if (Company is not null)
            OpenTaxReturnsRequested?.Invoke(Company.Id, SelectedFiscalYear?.Id);
    }

    private void OnCultureChanged()
    {
        if (!IsLoading && Company is not null)
            StatusMessage = Text("CompanyProfileLoaded");
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
