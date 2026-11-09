using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class OperationService : IOperationService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public OperationService(AlphaLedgerDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OfficeOperation>> GetOperationsAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Operations.AsNoTracking().AsQueryable();
        query = query
            .Include(operation => operation.Company)
            .Include(operation => operation.FiscalYear)
            .Include(operation => operation.AssignedEmployee);
        if (companyId.HasValue)
            query = query.Where(o => o.CompanyId == companyId.Value);
        return await query.OrderByDescending(o => o.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<OfficeOperation?> GetOperationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<OfficeOperation> AddOperationAsync(OfficeOperation operation, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(operation, cancellationToken);
        operation.ServiceCategory = operation.ServiceCategory.Trim();
        operation.Description = operation.Description.Trim();
        operation.TaxPeriod = string.IsNullOrWhiteSpace(operation.TaxPeriod) ? null : operation.TaxPeriod.Trim();
        operation.Notes = string.IsNullOrWhiteSpace(operation.Notes) ? null : operation.Notes.Trim();
        if (operation.CreatedAtUtc == default)
            operation.CreatedAtUtc = _clock.UtcNow;
        if (operation.Status == OperationStatus.Completed && operation.CompletionDate is null)
            operation.CompletionDate = _clock.TodayLocal;

        _db.Operations.Add(operation);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(OfficeOperation), operation.Id.ToString(),
            $"Created operation {operation.ServiceCategory} for company {operation.CompanyId}.", cancellationToken: cancellationToken);
        return operation;
    }

    public async Task<OfficeOperation> UpdateOperationAsync(Guid id, OfficeOperation operation, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new BusinessException("Operation not found.");
        if (operation.CompanyId != existing.CompanyId)
            throw new BusinessException("An operation cannot be moved to another company.");
        await ValidateReferencesAsync(operation, cancellationToken);
        if (operation.FiscalYearId != existing.FiscalYearId &&
            (await _db.Fees.AnyAsync(fee => fee.OperationId == id &&
                    fee.FiscalYearId.HasValue && fee.FiscalYearId != operation.FiscalYearId,
                cancellationToken) ||
             await _db.TaxReturns.AnyAsync(taxReturn => taxReturn.RelatedOperationId == id &&
                    taxReturn.FiscalYearId.HasValue && taxReturn.FiscalYearId != operation.FiscalYearId,
                cancellationToken)))
            throw new BusinessException("The operation cannot be moved to a fiscal year that conflicts with its linked fees or tax returns.");

        existing.ServiceCategory = operation.ServiceCategory.Trim();
        existing.Description = operation.Description.Trim();
        existing.TaxPeriod = string.IsNullOrWhiteSpace(operation.TaxPeriod) ? null : operation.TaxPeriod.Trim();
        existing.FiscalYearId = operation.FiscalYearId;
        existing.AssignedEmployeeId = operation.AssignedEmployeeId;
        existing.StartDate = operation.StartDate;
        existing.DueDate = operation.DueDate;
        existing.CompletionDate = operation.CompletionDate;
        existing.Status = operation.Status;
        existing.Priority = operation.Priority;
        existing.AgreedFee = operation.AgreedFee;
        existing.Notes = string.IsNullOrWhiteSpace(operation.Notes) ? null : operation.Notes.Trim();
        if (operation.Status == OperationStatus.Completed)
            existing.CompletionDate ??= _clock.TodayLocal;
        else
            existing.CompletionDate = null;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(OfficeOperation), existing.Id.ToString(),
            $"Updated operation {existing.ServiceCategory} to status {existing.Status}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteOperationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new BusinessException("Operation not found.");

        if (await _db.Fees.AnyAsync(fee => fee.OperationId == id, cancellationToken) ||
            await _db.TaxReturns.AnyAsync(taxReturn => taxReturn.RelatedOperationId == id, cancellationToken) ||
            await _db.Collections.AnyAsync(collection => collection.OperationId == id, cancellationToken))
            throw new BusinessException("An operation linked to a tax return, fee, or collection cannot be deleted. Mark it cancelled instead.");

        _db.Operations.Remove(op);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(OfficeOperation), id.ToString(),
            $"Deleted operation {op.ServiceCategory}.", cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<OfficeOperation>> GetOverdueOperationsAsync(CancellationToken cancellationToken = default)
    {
        var today = _clock.TodayLocal;
        return await _db.Operations
            .AsNoTracking()
            .Include(operation => operation.Company)
            .Include(operation => operation.FiscalYear)
            .Include(operation => operation.AssignedEmployee)
            .Where(o => o.DueDate.HasValue && o.DueDate.Value < today && o.Status != OperationStatus.Completed && o.Status != OperationStatus.Cancelled)
            .OrderBy(o => o.DueDate)
            .ToListAsync(cancellationToken);
    }

    private async Task ValidateReferencesAsync(OfficeOperation operation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.ServiceCategory))
            throw new BusinessException("Select an operation service type.");
        if (string.IsNullOrWhiteSpace(operation.Description))
            throw new BusinessException("Enter an operation description.");
        if (operation.AgreedFee < 0)
            throw new BusinessException("The agreed fee cannot be negative.");
        if (!await _db.Companies.AnyAsync(company => company.Id == operation.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
        if (operation.FiscalYearId.HasValue && !await _db.CompanyFiscalYears.AnyAsync(
                year => year.Id == operation.FiscalYearId.Value && year.CompanyId == operation.CompanyId,
                cancellationToken))
            throw new BusinessException("The selected fiscal year does not belong to this company.");
        if (operation.AssignedEmployeeId.HasValue && !await _db.Employees.AnyAsync(
                employee => employee.Id == operation.AssignedEmployeeId.Value,
                cancellationToken))
            throw new BusinessException("The selected employee was not found.");
        if (operation.StartDate.HasValue && operation.DueDate.HasValue &&
            operation.DueDate.Value < operation.StartDate.Value)
            throw new BusinessException("The due date cannot be earlier than the start date.");
        if (operation.StartDate.HasValue && operation.CompletionDate.HasValue &&
            operation.CompletionDate.Value < operation.StartDate.Value)
            throw new BusinessException("The completion date cannot be earlier than the start date.");
    }
}