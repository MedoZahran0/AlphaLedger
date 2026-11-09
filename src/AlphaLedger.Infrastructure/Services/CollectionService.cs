using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
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
        await RefreshFeeStatusAsync(collection.FeeId!.Value, collection.AmountReceived, null, cancellationToken);
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
        if (collection.CompanyId != existing.CompanyId || collection.FeeId != existing.FeeId)
            throw new BusinessException("A collection cannot be moved to another company or fee. Reverse it and record a new collection instead.");
        await ValidateCollectionAsync(collection, existing.Id, cancellationToken);

        var createdAtUtc = existing.CreatedAtUtc;
        _db.Entry(existing).CurrentValues.SetValues(collection);
        existing.CreatedAtUtc = createdAtUtc;
        existing.UpdatedAtUtc = _clock.UtcNow;
        await RefreshFeeStatusAsync(existing.FeeId!.Value, existing.AmountReceived, existing.Id, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(Collection), existing.Id.ToString(),
            $"Updated collection {existing.AmountReceived} for fee {existing.FeeId}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteCollectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _db.Collections.AnyAsync(collection => collection.Id == id, cancellationToken))
            throw new BusinessException("Collection not found.");

        throw new BusinessException("Collection transactions cannot be deleted. Reverse the collection with a reason to preserve the audit trail.");
    }

    public async Task<Collection> ReverseCollectionAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException("A reversal reason is required.");

        var collection = await _db.Collections.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new BusinessException("Collection not found.");
        if (collection.IsReversed)
            throw new BusinessException("This collection has already been reversed.");

        if (collection.FeeId is Guid feeId)
        {
            var fee = await _db.Fees.FirstOrDefaultAsync(item => item.Id == feeId, cancellationToken);
            if (fee is not null && fee.Status != FeeStatus.Cancelled)
            {
                var otherActiveCollections = await _db.Collections
                    .Where(item => item.FeeId == feeId && !item.IsReversed && item.Id != collection.Id)
                    .SumAsync(item => (decimal?)item.AmountReceived, cancellationToken) ?? 0m;
                fee.Status = otherActiveCollections == 0
                    ? FeeStatus.Agreed
                    : otherActiveCollections >= fee.Amount
                        ? FeeStatus.Collected
                        : FeeStatus.PartiallyCollected;
                fee.UpdatedAtUtc = _clock.UtcNow;
            }
        }

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
        var fee = await _db.Fees.AsNoTracking().FirstOrDefaultAsync(item => item.Id == feeId, cancellationToken)
            ?? throw new BusinessException("Fee not found.");
        if (fee.Status == FeeStatus.Cancelled)
            return false;
        var outstanding = await GetOutstandingBalanceAsync(feeId, cancellationToken);
        return amount > 0 && amount <= outstanding;
    }

    private async Task ValidateCollectionAsync(Collection collection, Guid? excludedCollectionId, CancellationToken cancellationToken)
    {
        if (collection.AmountReceived <= 0)
            throw new BusinessException("Collection amount must be greater than zero.");
        if (collection.CollectionDate == default)
            throw new BusinessException("A collection date is required.");
        if (string.IsNullOrWhiteSpace(collection.PaymentMethod))
            throw new BusinessException("Select a payment method.");
        if (!await _db.Companies.AnyAsync(company => company.Id == collection.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
        if (!collection.FeeId.HasValue)
            throw new BusinessException("Select the fee for this collection.");

        var fee = await _db.Fees.FirstOrDefaultAsync(item => item.Id == collection.FeeId.Value, cancellationToken)
            ?? throw new BusinessException("Fee not found.");
        if (fee.Status == FeeStatus.Cancelled)
            throw new BusinessException("A cancelled fee cannot receive a collection.");
        if (fee.CompanyId != collection.CompanyId)
            throw new BusinessException("The selected fee does not belong to this company.");
        if (collection.OperationId.HasValue && !await _db.Operations.AnyAsync(operation =>
                operation.Id == collection.OperationId.Value && operation.CompanyId == collection.CompanyId,
                cancellationToken))
            throw new BusinessException("The selected operation does not belong to this company.");
        if (collection.OperationId.HasValue && fee.OperationId.HasValue &&
            collection.OperationId != fee.OperationId)
            throw new BusinessException("The selected operation does not match the operation linked to this fee.");

        if (!string.IsNullOrWhiteSpace(collection.ReceiptNumber) && await _db.Collections.AnyAsync(item =>
                item.ReceiptNumber == collection.ReceiptNumber && item.Id != excludedCollectionId,
                cancellationToken))
            throw new BusinessException("This receipt number is already in use.");

        var collected = await _db.Collections
            .Where(item => item.FeeId == collection.FeeId && !item.IsReversed && item.Id != excludedCollectionId)
            .SumAsync(item => (decimal?)item.AmountReceived, cancellationToken) ?? 0m;
        if (collection.AmountReceived > fee.Amount - collected)
            throw new BusinessException("Collection amount exceeds the outstanding fee balance.");
        if (collection.ReceivedByEmployeeId.HasValue &&
            !await _db.Employees.AnyAsync(employee => employee.Id == collection.ReceivedByEmployeeId.Value, cancellationToken))
            throw new BusinessException("The selected receiving employee was not found.");
    }

    private async Task RefreshFeeStatusAsync(
        Guid feeId,
        decimal currentCollectionAmount,
        Guid? excludedCollectionId,
        CancellationToken cancellationToken)
    {
        var fee = await _db.Fees.FirstOrDefaultAsync(item => item.Id == feeId, cancellationToken)
            ?? throw new BusinessException("Fee not found.");
        if (fee.Status == FeeStatus.Cancelled)
            return;

        var otherCollections = await _db.Collections
            .Where(item => item.FeeId == feeId && !item.IsReversed && item.Id != excludedCollectionId)
            .SumAsync(item => (decimal?)item.AmountReceived, cancellationToken) ?? 0m;
        var totalCollected = otherCollections + currentCollectionAmount;
        fee.Status = totalCollected == fee.Amount
            ? FeeStatus.Collected
            : totalCollected > 0
                ? FeeStatus.PartiallyCollected
                : FeeStatus.Agreed;
        fee.UpdatedAtUtc = _clock.UtcNow;
    }
}