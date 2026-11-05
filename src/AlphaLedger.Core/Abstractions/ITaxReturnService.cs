using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;

namespace AlphaLedger.Core.Abstractions;

public interface ITaxReturnService
{
    Task<IReadOnlyList<TaxReturn>> GetTaxReturnsAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
    Task<TaxReturn> AddTaxReturnAsync(TaxReturn taxReturn, CancellationToken cancellationToken = default);
    Task<TaxReturn> UpdateTaxReturnAsync(Guid id, TaxReturn taxReturn, CancellationToken cancellationToken = default);
    Task DeleteTaxReturnAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TaxReturn?> GetTaxReturnAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaxReturn>> GetOverdueTaxReturnsAsync(CancellationToken cancellationToken = default);
    Task MarkSubmittedAsync(Guid id, string? receiptNumber, CancellationToken cancellationToken = default);
}