namespace AlphaLedger.Core.Entities;

public class Company
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
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
    public DateTime RecordCreatedAtUtc { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public string? FeeAgreementDetails { get; set; }
    public string? InternalNotes { get; set; }
    public bool IsArchived { get; set; }
    public string? DocumentStoragePath { get; set; }
    public int? ArchiveStartYear { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Employee? AssignedEmployee { get; set; }
    public ICollection<CompanyFiscalYear> FiscalYears { get; set; } = new List<CompanyFiscalYear>();
    public ICollection<OfficeOperation> Operations { get; set; } = new List<OfficeOperation>();
    public ICollection<Fee> Fees { get; set; } = new List<Fee>();
    public ICollection<Collection> Collections { get; set; } = new List<Collection>();
    public ICollection<TaxReturn> TaxReturns { get; set; } = new List<TaxReturn>();
    public ICollection<CompanyPortalAccount> PortalAccounts { get; set; } = new List<CompanyPortalAccount>();
}
