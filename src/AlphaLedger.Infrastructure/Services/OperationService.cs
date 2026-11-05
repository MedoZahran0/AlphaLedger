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

    public OperationService(AlphaLedgerDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<OfficeOperation>> GetOperationsAsync(Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Operations.AsNoTracking().AsQueryable();
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
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == operation.CompanyId, cancellationToken)
            ?? throw new BusinessException("Company not found.");

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

        existing.ServiceCategory = operation.ServiceCategory;
        existing.Description = operation.Description;
        existing.TaxPeriod = operation.TaxPeriod;
        existing.AssignedEmployeeId = operation.AssignedEmployeeId;
        existing.StartDate = operation.StartDate;
        existing.DueDate = operation.DueDate;
        existing.CompletionDate = operation.CompletionDate;
        existing.Status = operation.Status;
        existing.Priority = operation.Priority;
        existing.AgreedFee = operation.AgreedFee;
        existing.Notes = operation.Notes;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(OfficeOperation), existing.Id.ToString(),
            $"Updated operation {existing.ServiceCategory} to status {existing.Status}.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteOperationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var op = await _db.Operations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new BusinessException("Operation not found.");

        _db.Operations.Remove(op);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(OfficeOperation), id.ToString(),
            $"Deleted operation {op.ServiceCategory}.", cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<OfficeOperation>> GetOverdueOperationsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return await _db.Operations
            .AsNoTracking()
            .Where(o => o.DueDate.HasValue && o.DueDate.Value < today && o.Status != OperationStatus.Completed && o.Status != OperationStatus.Cancelled)
            .OrderBy(o => o.DueDate)
            .ToListAsync(cancellationToken);
    }
}