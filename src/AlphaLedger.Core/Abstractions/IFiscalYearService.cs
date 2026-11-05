using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;

namespace AlphaLedger.Core.Abstractions;

public interface IFiscalYearService
{
    Task<IReadOnlyList<CompanyFiscalYear>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<CompanyFiscalYear> AddFiscalYearAsync(Guid companyId, FiscalYearEditModel model, CancellationToken cancellationToken = default);
    Task<CompanyFiscalYear> UpdateFiscalYearAsync(Guid id, FiscalYearEditModel model, CancellationToken cancellationToken = default);
    Task DeleteFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);
    Task CloseFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);
    Task ReopenFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CompanyFiscalYear?> GetFiscalYearAsync(Guid id, CancellationToken cancellationToken = default);
}