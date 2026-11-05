using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;

namespace AlphaLedger.Core.Abstractions;

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyListItem>> GetCompaniesAsync(CompanyFilter filter, CancellationToken cancellationToken = default);
    Task<Company?> GetCompanyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanyFiscalYear>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DuplicateMatch>> FindLikelyDuplicatesAsync(CompanyEditModel model, CancellationToken cancellationToken = default);
    Task<Company> CreateCompanyAsync(CompanyEditModel model, CancellationToken cancellationToken = default);
    Task<Company> UpdateCompanyAsync(CompanyEditModel model, CancellationToken cancellationToken = default);
    Task ArchiveCompanyAsync(Guid id, CancellationToken cancellationToken = default);
    Task RestoreCompanyAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteCompanyAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CompanyFiscalYear> AddFiscalYearAsync(Guid companyId, FiscalYearEditModel model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken = default);
    Task<Employee> QuickAddEmployeeAsync(string fullName, string? jobTitle = null, CancellationToken cancellationToken = default);
    Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default);
    Task RelocateCompanyFolderAsync(Guid companyId, string newPath, CancellationToken cancellationToken = default);
}
