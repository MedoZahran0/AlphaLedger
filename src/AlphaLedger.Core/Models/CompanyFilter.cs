namespace AlphaLedger.Core.Models;

public class CompanyFilter
{
    public string? SearchText { get; set; }
    public bool IncludeArchived { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    public string? Activity { get; set; }
}
