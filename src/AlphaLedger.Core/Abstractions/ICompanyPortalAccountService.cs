using AlphaLedger.Core.Entities;

namespace AlphaLedger.Core.Abstractions;

public interface ICompanyPortalAccountService
{
    Task<IReadOnlyList<CompanyPortalAccount>> GetForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyPortalAccount> AddAsync(CompanyPortalAccount account, string? password, CancellationToken cancellationToken = default);
    Task<CompanyPortalAccount> UpdateAsync(CompanyPortalAccount account, string? newPassword, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}