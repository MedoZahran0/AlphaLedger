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

        if (status.HasValue)
            query = query.Where(f => f.Status == status.Value);

        return await query.OrderByDescending(f => f.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<Fee?> GetFeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Fees.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<Fee> AddFeeAsync(Fee fee, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(fee, cancellationToken);
        var now = _clock.UtcNow;
        if (fee.CreatedAtUtc == default)
            fee.CreatedAtUtc = now;
        fee.UpdatedAtUtc = now;

        _db.Fees.Add(fee);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(Fee), fee.Id.ToString(), $"Recorded fee {fee.Description}.", cancellationToken: cancellationToken);
        return fee;
    }

    public async Task<Fee> UpdateFeeAsync(Fee fee, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(fee, cancellationToken);
        var existing = await _db.Fees.FirstOrDefaultAsync(f => f.Id == fee.Id, cancellationToken)
            ?? throw new BusinessException("Fee not found.");

        var createdAtUtc = existing.CreatedAtUtc;
        _db.Entry(existing).CurrentValues.SetValues(fee);
        existing.CreatedAtUtc = createdAtUtc;
        existing.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(Fee), existing.Id.ToString(), $"Updated fee {existing.Description}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteFeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fee = await _db.Fees.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new BusinessException("Fee not found.");

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
        var query = _db.Fees.AsNoTracking().Where(f => f.Status != FeeStatus.Cancelled);

        if (companyId.HasValue)
            query = query.Where(f => f.CompanyId == companyId.Value);

        return await query.SumAsync(f => f.Amount, cancellationToken);
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
    }

    public async Task<decimal> GetTotalCollectedAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Collections.AsNoTracking().Where(c => c.IsReversed == false);

        if (companyId.HasValue)
            query = query.Where(c => c.CompanyId == companyId.Value);

        return await query.SumAsync(c => c.AmountReceived, cancellationToken);
    }
}