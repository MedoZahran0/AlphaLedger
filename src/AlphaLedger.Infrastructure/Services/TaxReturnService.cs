using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class TaxReturnService : ITaxReturnService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public TaxReturnService(AlphaLedgerDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<TaxReturn>> GetTaxReturnsAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.TaxReturns.AsNoTracking().AsQueryable();
        if (companyId.HasValue)
            query = query.Where(t => t.CompanyId == companyId.Value);
        return await query.OrderByDescending(t => t.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<TaxReturn?> GetTaxReturnAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.TaxReturns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<TaxReturn> AddTaxReturnAsync(TaxReturn taxReturn, CancellationToken cancellationToken = default)
    {
        await ValidateReturnAsync(taxReturn, excludedId: null, cancellationToken);
        var now = _clock.UtcNow;
        taxReturn.CreatedAtUtc = taxReturn.CreatedAtUtc == default ? now : taxReturn.CreatedAtUtc;
        taxReturn.UpdatedAtUtc = now;

        _db.TaxReturns.Add(taxReturn);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(TaxReturn), taxReturn.Id.ToString(),
            $"Created {taxReturn.TaxType} tax return for company {taxReturn.CompanyId}.", cancellationToken: cancellationToken);
        return taxReturn;
    }

    public async Task<TaxReturn> UpdateTaxReturnAsync(Guid id, TaxReturn taxReturn, CancellationToken cancellationToken = default)
    {
        var existing = await _db.TaxReturns.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new BusinessException("Tax return not found.");
        await ValidateReturnAsync(taxReturn, id, cancellationToken);

        existing.FiscalYearId = taxReturn.FiscalYearId;
        existing.TaxType = taxReturn.TaxType;
        existing.TaxPeriod = taxReturn.TaxPeriod;
        existing.FilingDueDate = taxReturn.FilingDueDate;
        existing.PreparationStatus = taxReturn.PreparationStatus;
        existing.ReviewStatus = taxReturn.ReviewStatus;
        existing.FilingStatus = taxReturn.FilingStatus;
        existing.SubmissionDate = taxReturn.SubmissionDate;
        existing.OfficialReceiptNumber = taxReturn.OfficialReceiptNumber;
        existing.ResponsibleEmployeeId = taxReturn.ResponsibleEmployeeId;
        existing.RelatedOperationId = taxReturn.RelatedOperationId;
        existing.RelatedFeeId = taxReturn.RelatedFeeId;
        existing.Notes = taxReturn.Notes;
        existing.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(TaxReturn), id.ToString(),
            $"Updated {existing.TaxType} tax return status to {existing.FilingStatus}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteTaxReturnAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tr = await _db.TaxReturns.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new BusinessException("Tax return not found.");

        _db.TaxReturns.Remove(tr);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(TaxReturn), id.ToString(), "Deleted a tax return.", cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<TaxReturn>> GetOverdueTaxReturnsAsync(CancellationToken cancellationToken = default)
    {
        var today = _clock.TodayLocal;
        return await _db.TaxReturns
            .AsNoTracking()
            .Where(t => t.FilingDueDate < today && t.FilingStatus != TaxReturnFilingStatus.Submitted)
            .OrderBy(t => t.FilingDueDate)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkSubmittedAsync(Guid id, string? receiptNumber, CancellationToken cancellationToken = default)
    {
        var tr = await _db.TaxReturns.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new BusinessException("Tax return not found.");

        tr.FilingStatus = TaxReturnFilingStatus.Submitted;
        tr.SubmissionDate = _clock.TodayLocal;
        tr.OfficialReceiptNumber = receiptNumber;
        tr.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("MarkSubmitted", nameof(TaxReturn), id.ToString(),
            $"Marked tax return {tr.TaxType} as submitted.", cancellationToken: cancellationToken);
    }

    private async Task ValidateReturnAsync(TaxReturn taxReturn, Guid? excludedId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taxReturn.TaxType))
            throw new BusinessException("Tax type is required.");
        if (string.IsNullOrWhiteSpace(taxReturn.TaxPeriod))
            throw new BusinessException("Tax period is required.");
        if (!await _db.Companies.AnyAsync(company => company.Id == taxReturn.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
        if (taxReturn.FiscalYearId.HasValue && !await _db.CompanyFiscalYears.AnyAsync(
                year => year.Id == taxReturn.FiscalYearId.Value && year.CompanyId == taxReturn.CompanyId,
                cancellationToken))
            throw new BusinessException("The selected fiscal year does not belong to this company.");

        if (await _db.TaxReturns.AnyAsync(item => item.Id != excludedId &&
                item.CompanyId == taxReturn.CompanyId && item.TaxType == taxReturn.TaxType && item.TaxPeriod == taxReturn.TaxPeriod,
                cancellationToken))
            throw new BusinessException("A tax return for this type and period already exists for the company.");
    }
}