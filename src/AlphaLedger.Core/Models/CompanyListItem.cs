namespace AlphaLedger.Core.Models;

public class CompanyListItem
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LegalNameAr { get; set; } = string.Empty;
    public string? TaxRegistrationNumber { get; set; }
    public string? TaxFileNumber { get; set; }
    public string? TaxOffice { get; set; }
    public string? BusinessActivity { get; set; }
    public string? AssignedEmployeeName { get; set; }
    public DateOnly? ClientSince { get; set; }
    public int OperationsCount { get; set; }
    public decimal TotalAgreedFees { get; set; }
    public decimal TotalCollections { get; set; }
    public decimal RemainingFees { get; set; }
    public int OutstandingOperations { get; set; }
    public DateOnly? NextTaxDeadline { get; set; }
    public bool IsArchived { get; set; }
    public string StatusText => IsArchived ? "مؤرشف" : "نشط";
    public int? ArchiveStartYear { get; set; }
    public string? DocumentStoragePath { get; set; }
    public bool FolderMissing { get; set; }
    public bool FolderPathAssigned => !string.IsNullOrWhiteSpace(DocumentStoragePath);
    public string FolderStatusText => FolderMissing ? "مفقود" : "متوفر";
}
