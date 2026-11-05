using System.Collections.ObjectModel;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using AlphaLedger.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlphaLedger.App.ViewModels;

public partial class CompanyEditViewModel : ObservableObject
{
    private readonly ICompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly WindowsDialogService _dialogs;
    private readonly Guid? _companyId;

    public CompanyEditViewModel(
        ICompanyService companyService,
        ISettingsService settingsService,
        WindowsDialogService dialogs,
        Guid companyId = default)
    {
        _companyService = companyService;
        _settingsService = settingsService;
        _dialogs = dialogs;
        _companyId = companyId == Guid.Empty ? null : companyId;

        IsEdit = _companyId.HasValue;
        Title = Text(IsEdit ? "CompanyEditEditTitle" : "CompanyEditAddTitle");
        LegalForm = Text("CompanyEditLegalFormLlc");
        LocalizationService.Instance.CultureChanged += OnCultureChanged;

        var today = DateOnly.FromDateTime(DateTime.Now);
        ArchiveStartYear = today.Year;
        for (var year = today.Year + 10; year >= today.Year - 25; year--)
        {
            ArchiveStartYears.Add(year);
        }
        ClientSince = today;
    }

    public bool IsEdit { get; }
    [ObservableProperty] private string _title = string.Empty;

    [ObservableProperty] private string _legalNameAr = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string? _englishName;
    [ObservableProperty] private string? _taxRegistrationNumber;
    [ObservableProperty] private string? _taxFileNumber;
    [ObservableProperty] private string? _taxOffice;
    [ObservableProperty] private string? _businessActivity;
    [ObservableProperty] private string? _legalForm;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _phoneNumbers;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _contactPerson;
    [ObservableProperty] private string? _contactDetails;
    [ObservableProperty] private DateOnly? _clientSince;
    [ObservableProperty] private Guid? _assignedEmployeeId;
    [ObservableProperty] private string? _newEmployeeName;
    [ObservableProperty] private string? _feeAgreementDetails;
    [ObservableProperty] private string? _internalNotes;
    [ObservableProperty] private int _archiveStartYear;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _duplicateWarning;
    [ObservableProperty] private bool _hasDuplicateWarning;
    [ObservableProperty] private bool _ignoreDuplicateWarning;

    public ObservableCollection<Employee> Employees { get; } = [];
    public ObservableCollection<int> ArchiveStartYears { get; } = [];

    public event Action<bool>? CloseRequested;

    public async Task InitializeAsync()
    {
        try
        {
            var employees = await _companyService.GetActiveEmployeesAsync();
            Employees.Clear();
            foreach (var emp in employees)
            {
                Employees.Add(emp);
            }

            if (IsEdit && _companyId.HasValue)
            {
                var company = await _companyService.GetCompanyAsync(_companyId.Value);
                if (company != null)
                {
                    LegalNameAr = company.LegalNameAr;
                    DisplayName = company.DisplayName;
                    EnglishName = company.EnglishName;
                    TaxRegistrationNumber = company.TaxRegistrationNumber;
                    TaxFileNumber = company.TaxFileNumber;
                    TaxOffice = company.TaxOffice;
                    BusinessActivity = company.BusinessActivity;
                    LegalForm = company.LegalForm;
                    Address = company.Address;
                    PhoneNumbers = company.PhoneNumbers;
                    Email = company.Email;
                    ContactPerson = company.ContactPerson;
                    ContactDetails = company.ContactDetails;
                    ClientSince = company.ClientSince;
                    ArchiveStartYear = company.ArchiveStartYear ?? DateTime.Today.Year;
                    if (!ArchiveStartYears.Contains(ArchiveStartYear))
                    {
                        ArchiveStartYears.Add(ArchiveStartYear);
                        var sortedYears = ArchiveStartYears.OrderByDescending(year => year).ToList();
                        ArchiveStartYears.Clear();
                        foreach (var year in sortedYears)
                            ArchiveStartYears.Add(year);
                    }
                    AssignedEmployeeId = company.AssignedEmployeeId;
                    FeeAgreementDetails = company.FeeAgreementDetails;
                    InternalNotes = company.InternalNotes;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(LocalizationService.Instance.CurrentCulture,
                Text("CompanyEditLoadError"), LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        ErrorMessage = null;
        DuplicateWarning = null;
        HasDuplicateWarning = false;

        if (string.IsNullOrWhiteSpace(LegalNameAr))
        {
            ErrorMessage = Text("CompanyEditLegalNameRequired");
            return;
        }

        var root = await _settingsService.GetDocumentRootAsync();
        if (string.IsNullOrWhiteSpace(root) && !IsEdit)
        {
            ErrorMessage = Text("CompanyEditDocumentRootRequired");
            return;
        }

        var model = new CompanyEditModel
        {
            Id = _companyId,
            LegalNameAr = LegalNameAr.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? LegalNameAr.Trim() : DisplayName.Trim(),
            EnglishName = string.IsNullOrWhiteSpace(EnglishName) ? null : EnglishName.Trim(),
            TaxRegistrationNumber = string.IsNullOrWhiteSpace(TaxRegistrationNumber) ? null : TaxRegistrationNumber.Trim(),
            TaxFileNumber = string.IsNullOrWhiteSpace(TaxFileNumber) ? null : TaxFileNumber.Trim(),
            TaxOffice = string.IsNullOrWhiteSpace(TaxOffice) ? null : TaxOffice.Trim(),
            BusinessActivity = string.IsNullOrWhiteSpace(BusinessActivity) ? null : BusinessActivity.Trim(),
            LegalForm = string.IsNullOrWhiteSpace(LegalForm) ? null : LegalForm.Trim(),
            Address = string.IsNullOrWhiteSpace(Address) ? null : Address.Trim(),
            PhoneNumbers = string.IsNullOrWhiteSpace(PhoneNumbers) ? null : PhoneNumbers.Trim(),
            Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(ContactPerson) ? null : ContactPerson.Trim(),
            ContactDetails = string.IsNullOrWhiteSpace(ContactDetails) ? null : ContactDetails.Trim(),
            ClientSince = ClientSince,
            AssignedEmployeeId = AssignedEmployeeId,
            NewEmployeeName = string.IsNullOrWhiteSpace(NewEmployeeName) ? null : NewEmployeeName.Trim(),
            FeeAgreementDetails = string.IsNullOrWhiteSpace(FeeAgreementDetails) ? null : FeeAgreementDetails.Trim(),
            InternalNotes = string.IsNullOrWhiteSpace(InternalNotes) ? null : InternalNotes.Trim(),
            ArchiveStartYear = ArchiveStartYear,
            CreateCurrentFiscalYear = false,
            IgnoreDuplicateWarning = IgnoreDuplicateWarning
        };

        try
        {
            if (IsEdit)
            {
                await _companyService.UpdateCompanyAsync(model);
                _dialogs.Info(Text("CompanyEditSaved"));
            }
            else
            {
                await _companyService.CreateCompanyAsync(model);
                _dialogs.Info(Text("CompanyEditCreated"));
            }

            CloseRequested?.Invoke(true);
        }
        catch (DuplicateCompanyException ex)
        {
            DuplicateWarning = LocalizationService.Instance.TranslateMessage(ex.Message);
            HasDuplicateWarning = true;
            IgnoreDuplicateWarning = true; // Next click will force save
            ErrorMessage = Text("CompanyEditDuplicateWarning");
        }
        catch (BusinessException ex)
        {
            ErrorMessage = LocalizationService.Instance.TranslateMessage(ex.Message);
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(LocalizationService.Instance.CurrentCulture,
                Text("CompanyEditSaveFailed"), LocalizationService.Instance.TranslateMessage(ex.Message));
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    private void OnCultureChanged()
    {
        Title = Text(IsEdit ? "CompanyEditEditTitle" : "CompanyEditAddTitle");
        if (string.IsNullOrWhiteSpace(LegalForm) ||
            LegalForm is "Limited Liability Company" or "شركة ذات مسؤولية محدودة")
            LegalForm = Text("CompanyEditLegalFormLlc");
    }

    private static string Text(string key) => LocalizationService.Instance.GetString(key);
}
