namespace AlphaLedger.Core.Models;

public class CompanyEditModel
{
    public Guid? Id { get; set; }
    public string LegalNameAr { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? EnglishName { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? TaxFileNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? BusinessActivity { get; set; }
    public string? LegalForm { get; set; }
    public string? Address { get; set; }
    public string? PhoneNumbers { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactDetails { get; set; }
    public DateOnly? ClientSince { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public string? NewEmployeeName { get; set; }
    public string? FeeAgreementDetails { get; set; }
    public string? InternalNotes { get; set; }
    public int? ArchiveStartYear { get; set; }
    public bool CreateCurrentFiscalYear { get; set; } = true;
    public DateOnly? FiscalYearStart { get; set; }
    public DateOnly? FiscalYearEnd { get; set; }
    public bool IgnoreDuplicateWarning { get; set; }
}
