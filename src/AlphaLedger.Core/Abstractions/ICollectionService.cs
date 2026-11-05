using AlphaLedger.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlphaLedger.Core.Abstractions;

public interface ICollectionService
{
    Task<IReadOnlyList<Collection>> GetCollectionsAsync(Guid? companyId = null, CancellationToken cancellationToken = default);
    Task<Collection?> GetCollectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Collection> AddCollectionAsync(Collection collection, CancellationToken cancellationToken = default);
    Task<Collection> UpdateCollectionAsync(Collection collection, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Collection> ReverseCollectionAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<decimal> GetOutstandingBalanceAsync(Guid feeId, CancellationToken cancellationToken = default);
    Task<bool> CanCollectAsync(Guid feeId, decimal amount, CancellationToken cancellationToken = default);
}