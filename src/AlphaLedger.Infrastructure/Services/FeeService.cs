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

public sealed class FeeService : IFeeService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public FeeService(AlphaLedgerDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<Fee>> GetFeesAsync(Guid? companyId = null, FeeStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Fees.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(f => f.CompanyId == companyId.Value);

        var fees = await query.OrderByDescending(f => f.CreatedAtUtc).ToListAsync(cancellationToken);
        await RefreshCollectionStatusesAsync(fees, cancellationToken);
        return status.HasValue
            ? fees.Where(fee => fee.Status == status.Value).ToArray()
            : fees;
    }

    public async Task<Fee?> GetFeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fee = await _db.Fees.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (fee is not null)
            await RefreshCollectionStatusesAsync([fee], cancellationToken);
        return fee;
    }

    public async Task<Fee> AddFeeAsync(Fee fee, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(fee, cancellationToken);
        await EnsureCollectionsFitAmountAsync(fee.Id, fee.Amount, cancellationToken);
        var now = _clock.UtcNow;
        if (fee.CreatedAtUtc == default)
            fee.CreatedAtUtc = now;
        fee.UpdatedAtUtc = now;
        fee.Description = fee.Description.Trim();
        fee.Currency = string.IsNullOrWhiteSpace(fee.Currency) ? "EGP" : fee.Currency.Trim();
        fee.Notes = string.IsNullOrWhiteSpace(fee.Notes) ? null : fee.Notes.Trim();
        await RefreshCollectionStatusAsync(fee, cancellationToken);

        _db.Fees.Add(fee);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(Fee), fee.Id.ToString(), $"Recorded fee {fee.Description}.", cancellationToken: cancellationToken);
        return fee;
    }

    public async Task<Fee> UpdateFeeAsync(Fee fee, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Fees.FirstOrDefaultAsync(f => f.Id == fee.Id, cancellationToken)
            ?? throw new BusinessException("Fee not found.");
        if (fee.CompanyId != existing.CompanyId)
            throw new BusinessException("A fee cannot be moved to another company.");
        await ValidateReferencesAsync(fee, cancellationToken);
        await EnsureCollectionsFitAmountAsync(fee.Id, fee.Amount, cancellationToken);
        if (fee.Status == FeeStatus.Cancelled &&
            await _db.Collections.AnyAsync(
                collection => collection.FeeId == fee.Id && !collection.IsReversed,
                cancellationToken))
            throw new BusinessException("A fee with active collections cannot be cancelled. Reverse the collections first.");

        var createdAtUtc = existing.CreatedAtUtc;
        _db.Entry(existing).CurrentValues.SetValues(fee);
        existing.CreatedAtUtc = createdAtUtc;
        existing.UpdatedAtUtc = _clock.UtcNow;
        existing.Description = fee.Description.Trim();
        existing.Currency = string.IsNullOrWhiteSpace(fee.Currency) ? "EGP" : fee.Currency.Trim();
        existing.Notes = string.IsNullOrWhiteSpace(fee.Notes) ? null : fee.Notes.Trim();
        await RefreshCollectionStatusAsync(existing, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(Fee), existing.Id.ToString(), $"Updated fee {existing.Description}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteFeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fee = await _db.Fees.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new BusinessException("Fee not found.");

        if (await _db.Collections.AnyAsync(collection => collection.FeeId == id, cancellationToken))
            throw new BusinessException("A fee with collection history cannot be deleted. Cancel the fee instead to preserve its financial record.");
        if (await _db.TaxReturns.AnyAsync(taxReturn => taxReturn.RelatedFeeId == id, cancellationToken))
            throw new BusinessException("A fee linked to a tax return cannot be deleted. Cancel the fee instead to preserve the relationship.");

        _db.Fees.Remove(fee);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(Fee), id.ToString(), "Deleted a fee.", cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetCollectionsByFeeAsync(Guid feeId, CancellationToken cancellationToken = default)
    {
        return await _db.Collections.AsNoTracking()
            .Where(c => c.FeeId == feeId)
            .OrderByDescending(c => c.CollectionDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetTotalAgreedFeesAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var fees = await GetFeesAsync(companyId, cancellationToken: cancellationToken);
        return fees
            .Where(fee => fee.Status is not FeeStatus.Cancelled and not FeeStatus.Draft)
            .Sum(fee => fee.Amount);
    }

    private async Task ValidateReferencesAsync(Fee fee, CancellationToken cancellationToken)
    {
        if (fee.Amount <= 0)
            throw new BusinessException("Fee amount must be greater than zero.");
        if (!await _db.Companies.AnyAsync(company => company.Id == fee.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
        if (fee.FiscalYearId.HasValue && !await _db.CompanyFiscalYears.AnyAsync(
                year => year.Id == fee.FiscalYearId.Value && year.CompanyId == fee.CompanyId,
                cancellationToken))
            throw new BusinessException("The selected fiscal year does not belong to this company.");
        if (fee.OperationId.HasValue)
        {
            var operation = await _db.Operations.FirstOrDefaultAsync(
                item => item.Id == fee.OperationId.Value && item.CompanyId == fee.CompanyId,
                cancellationToken);
            if (operation is null)
                throw new BusinessException("The selected operation does not belong to this company.");
            if (fee.FiscalYearId.HasValue && operation.FiscalYearId.HasValue &&
                operation.FiscalYearId != fee.FiscalYearId)
                throw new BusinessException("The selected operation belongs to a different fiscal year.");
        }
    }

    public async Task<decimal> GetTotalCollectedAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Collections.AsNoTracking().Where(c => c.IsReversed == false);

        if (companyId.HasValue)
            query = query.Where(c => c.CompanyId == companyId.Value);

        return await query.SumAsync(c => c.AmountReceived, cancellationToken);
    }

    private async Task EnsureCollectionsFitAmountAsync(Guid feeId, decimal amount, CancellationToken cancellationToken)
    {
        var collected = await _db.Collections
            .Where(collection => collection.FeeId == feeId && !collection.IsReversed)
            .SumAsync(collection => (decimal?)collection.AmountReceived, cancellationToken) ?? 0m;
        if (collected > amount)
            throw new BusinessException("The fee amount cannot be lower than the total of its active collections.");
    }

    private async Task RefreshCollectionStatusAsync(Fee fee, CancellationToken cancellationToken)
    {
        if (fee.Status == FeeStatus.Cancelled)
        {
            if (await _db.Collections.AnyAsync(
                    collection => collection.FeeId == fee.Id && !collection.IsReversed,
                    cancellationToken))
                throw new BusinessException("A fee with active collections cannot be cancelled. Reverse the collections first.");
            return;
        }

        var collected = await _db.Collections
            .Where(collection => collection.FeeId == fee.Id && !collection.IsReversed)
            .SumAsync(collection => (decimal?)collection.AmountReceived, cancellationToken) ?? 0m;
        if (collected == 0m)
        {
            if (fee.Status is FeeStatus.PartiallyCollected or FeeStatus.Collected)
                fee.Status = FeeStatus.Agreed;
            return;
        }

        fee.Status = collected >= fee.Amount ? FeeStatus.Collected : FeeStatus.PartiallyCollected;
    }

    private async Task RefreshCollectionStatusesAsync(
        IReadOnlyCollection<Fee> fees,
        CancellationToken cancellationToken)
    {
        if (fees.Count == 0)
            return;

        var feeIds = fees.Select(fee => fee.Id).ToArray();
        var totals = await _db.Collections
            .Where(collection => collection.FeeId.HasValue && feeIds.Contains(collection.FeeId.Value) && !collection.IsReversed)
            .GroupBy(collection => collection.FeeId!.Value)
            .Select(group => new { FeeId = group.Key, Total = group.Sum(collection => collection.AmountReceived) })
            .ToDictionaryAsync(group => group.FeeId, group => group.Total, cancellationToken);

        foreach (var fee in fees)
        {
            if (fee.Status == FeeStatus.Cancelled)
                continue;

            var collected = totals.GetValueOrDefault(fee.Id);
            fee.Status = collected >= fee.Amount
                ? FeeStatus.Collected
                : collected > 0
                    ? FeeStatus.PartiallyCollected
                    : fee.Status is FeeStatus.PartiallyCollected or FeeStatus.Collected
                        ? FeeStatus.Agreed
                        : fee.Status;
        }
    }
}