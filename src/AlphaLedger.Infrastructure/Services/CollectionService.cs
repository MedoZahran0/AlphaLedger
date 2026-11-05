using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlphaLedger.Infrastructure.Services;

public sealed class CollectionService : ICollectionService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public CollectionService(AlphaLedgerDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Collection>> GetCollectionsAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Collections.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(c => c.CompanyId == companyId.Value);

        return await query.OrderByDescending(c => c.CollectionDate).ToListAsync(cancellationToken);
    }

    public async Task<Collection?> GetCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Collections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Collection> AddCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        await ValidateCollectionAsync(collection, excludedCollectionId: null, cancellationToken);
        var now = _clock.UtcNow;
        if (collection.CreatedAtUtc == default)
            collection.CreatedAtUtc = now;
        collection.UpdatedAtUtc = now;

        _db.Collections.Add(collection);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(Collection), collection.Id.ToString(),
            $"Recorded collection {collection.AmountReceived} for fee {collection.FeeId}.", cancellationToken: cancellationToken);
        return collection;
    }

    public async Task<Collection> UpdateCollectionAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Collections.FirstOrDefaultAsync(c => c.Id == collection.Id, cancellationToken)
            ?? throw new BusinessException("Collection not found.");
        if (existing.IsReversed)
            throw new BusinessException("A reversed collection cannot be edited.");
        await ValidateCollectionAsync(collection, existing.Id, cancellationToken);

        var createdAtUtc = existing.CreatedAtUtc;
        _db.Entry(existing).CurrentValues.SetValues(collection);
        existing.CreatedAtUtc = createdAtUtc;
        existing.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(Collection), existing.Id.ToString(),
            $"Updated collection {existing.AmountReceived} for fee {existing.FeeId}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _db.Collections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new BusinessException("Collection not found.");

        _db.Collections.Remove(collection);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(Collection), id.ToString(), "Deleted a collection.", cancellationToken: cancellationToken);
    }

    public async Task<Collection> ReverseCollectionAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("A reversal reason is required.");

        var collection = await _db.Collections.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new BusinessException("Collection not found.");
        if (collection.IsReversed)
            throw new BusinessException("This collection has already been reversed.");

        collection.IsReversed = true;
        collection.ReversalReason = reason.Trim();
        collection.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Reverse", nameof(Collection), id.ToString(),
            $"Reversed collection {collection.AmountReceived} for fee {collection.FeeId}. Reason: {collection.ReversalReason}",
            cancellationToken: cancellationToken);
        return collection;
    }

    public async Task<decimal> GetOutstandingBalanceAsync(Guid feeId, CancellationToken cancellationToken = default)
    {
        var fee = await _db.Fees.AsNoTracking().FirstOrDefaultAsync(f => f.Id == feeId, cancellationToken)
            ?? throw new BusinessException("Fee not found.");

        var collections = await _db.Collections.AsNoTracking()
            .Where(c => c.FeeId == feeId && c.IsReversed == false)
            .ToListAsync(cancellationToken);

        var totalCollected = collections.Sum(c => c.AmountReceived);
        return fee.Amount - totalCollected;
    }

    public async Task<bool> CanCollectAsync(Guid feeId, decimal amount, CancellationToken cancellationToken = default)
    {
        var outstanding = await GetOutstandingBalanceAsync(feeId, cancellationToken);
        return amount > 0 && amount <= outstanding;
    }

    private async Task ValidateCollectionAsync(Collection collection, Guid? excludedCollectionId, CancellationToken cancellationToken)
    {
        if (collection.AmountReceived <= 0)
            throw new BusinessException("Collection amount must be greater than zero.");
        if (!await _db.Companies.AnyAsync(company => company.Id == collection.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
        if (!collection.FeeId.HasValue)
            throw new BusinessException("Select the fee for this collection.");

        var fee = await _db.Fees.FirstOrDefaultAsync(item => item.Id == collection.FeeId.Value, cancellationToken)
            ?? throw new BusinessException("Fee not found.");
        if (fee.CompanyId != collection.CompanyId)
            throw new BusinessException("The selected fee does not belong to this company.");

        if (!string.IsNullOrWhiteSpace(collection.ReceiptNumber) && await _db.Collections.AnyAsync(item =>
                item.ReceiptNumber == collection.ReceiptNumber && item.Id != excludedCollectionId,
                cancellationToken))
            throw new BusinessException("This receipt number is already in use.");

        var collected = await _db.Collections
            .Where(item => item.FeeId == collection.FeeId && !item.IsReversed && item.Id != excludedCollectionId)
            .SumAsync(item => (decimal?)item.AmountReceived, cancellationToken) ?? 0m;
        if (collection.AmountReceived > fee.Amount - collected)
            throw new BusinessException("Collection amount exceeds the outstanding fee balance.");
    }
}