using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlphaLedger.Core.Abstractions;

public interface IFeeService
{
    Task<IReadOnlyList<Fee>> GetFeesAsync(Guid? companyId = null, FeeStatus? status = null, CancellationToken cancellationToken = default);
    Task<Fee?> GetFeeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Fee> AddFeeAsync(Fee fee, CancellationToken cancellationToken = default);
    Task<Fee> UpdateFeeAsync(Fee fee, CancellationToken cancellationToken = default);
    Task DeleteFeeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Collection>> GetCollectionsByFeeAsync(Guid feeId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalAgreedFeesAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalCollectedAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
}