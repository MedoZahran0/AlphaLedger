using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class FiscalYearService : IFiscalYearService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IAuditService _audit;
    private readonly ISettingsService _settings;
    private readonly ICompanyFolderService _folders;

    public FiscalYearService(AlphaLedgerDbContext db, IAuditService audit, ISettingsService settings, ICompanyFolderService folders)
    {
        _db = db;
        _audit = audit;
        _settings = settings;
        _folders = folders;
    }

    public async Task<IReadOnlyList<CompanyFiscalYear>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        return await _db.CompanyFiscalYears
            .AsNoTracking()
            .Where(y => y.CompanyId == companyId)
            .OrderByDescending(y => y.StartDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyFiscalYear?> GetFiscalYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.CompanyFiscalYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == id, cancellationToken);
    }

    public async Task<CompanyFiscalYear> AddFiscalYearAsync(Guid companyId, FiscalYearEditModel model, CancellationToken cancellationToken = default)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            ?? throw new BusinessException("Company not found.");

        if (model.EndDate <= model.StartDate)
            throw new BusinessException("End date must be after start date.");

        var overlaps = await _db.CompanyFiscalYears
            .AnyAsync(y => y.CompanyId == companyId &&
                          ((model.StartDate >= y.StartDate && model.StartDate <= y.EndDate) ||
                           (model.EndDate >= y.StartDate && model.EndDate <= y.EndDate) ||
                           (model.StartDate <= y.StartDate && model.EndDate >= y.EndDate)), cancellationToken);
        if (overlaps)
            throw new BusinessException("This fiscal year overlaps with an existing year.");

        if (model.Status == FiscalYearStatus.Active)
        {
            var activeYears = await _db.CompanyFiscalYears
                .Where(y => y.CompanyId == companyId && y.Status == FiscalYearStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var ay in activeYears)
                ay.Status = FiscalYearStatus.Closed;
        }

        var year = new CompanyFiscalYear
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            YearLabel = model.YearLabel,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = model.Status,
            Notes = model.Notes,
            CreatedAtUtc = DateTime.UtcNow
        };

        var documentRoot = await _settings.GetDocumentRootAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(documentRoot))
            throw new BusinessException("اختر مجلد الأرشيف من الإعدادات أولاً.");
        year.FolderPath = _folders.EnsureFiscalYearFolder(documentRoot, company, year);
        if (string.IsNullOrWhiteSpace(company.DocumentStoragePath))
            company.DocumentStoragePath = _folders.EnsureCurrentYearCompanyFolder(documentRoot, company);
        _db.CompanyFiscalYears.Add(year);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "AddFiscalYear",
            nameof(CompanyFiscalYear),
            year.Id.ToString(),
            $"Added fiscal year: {model.YearLabel} from {model.StartDate:yyyy-MM-dd} to {model.EndDate:yyyy-MM-dd}",
            cancellationToken: cancellationToken);

        return year;
    }

    public async Task<CompanyFiscalYear> UpdateFiscalYearAsync(Guid id, FiscalYearEditModel model, CancellationToken cancellationToken = default)
    {
        var year = await _db.CompanyFiscalYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw new BusinessException("Fiscal year not found.");

        if (year.Status == FiscalYearStatus.Closed)
            throw new BusinessException("Cannot modify a closed fiscal year.");

        if (model.EndDate <= model.StartDate)
            throw new BusinessException("End date must be after start date.");

        var overlaps = await _db.CompanyFiscalYears
            .AnyAsync(y => y.Id != id && y.CompanyId == year.CompanyId &&
                          ((model.StartDate >= y.StartDate && model.StartDate <= y.EndDate) ||
                           (model.EndDate >= y.StartDate && model.EndDate <= y.EndDate) ||
                           (model.StartDate <= y.StartDate && model.EndDate >= y.EndDate)), cancellationToken);
        if (overlaps)
            throw new BusinessException("This fiscal year overlaps with an existing year.");

        if (model.Status == FiscalYearStatus.Active)
        {
            var activeYears = await _db.CompanyFiscalYears
                .Where(y => y.CompanyId == year.CompanyId && y.Status == FiscalYearStatus.Active && y.Id != id)
                .ToListAsync(cancellationToken);
            foreach (var ay in activeYears)
                ay.Status = FiscalYearStatus.Closed;
        }

        year.YearLabel = model.YearLabel;
        year.StartDate = model.StartDate;
        year.EndDate = model.EndDate;
        year.Status = model.Status;
        year.Notes = model.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "UpdateFiscalYear",
            nameof(CompanyFiscalYear),
            id.ToString(),
            $"Updated fiscal year: {model.YearLabel}",
            cancellationToken: cancellationToken);

        return year;
    }

    public async Task DeleteFiscalYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var year = await _db.CompanyFiscalYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw new BusinessException("Fiscal year not found.");

        if (year.Status == FiscalYearStatus.Active)
            throw new BusinessException("Cannot delete an active fiscal year. Close it first.");

        _db.CompanyFiscalYears.Remove(year);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "DeleteFiscalYear",
            nameof(CompanyFiscalYear),
            id.ToString(),
            $"Deleted fiscal year: {year.YearLabel}",
            cancellationToken: cancellationToken);
    }

    public async Task CloseFiscalYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var year = await _db.CompanyFiscalYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw new BusinessException("Fiscal year not found.");

        if (year.Status == FiscalYearStatus.Closed)
            throw new BusinessException("Fiscal year is already closed.");

        year.Status = FiscalYearStatus.Closed;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "CloseFiscalYear",
            nameof(CompanyFiscalYear),
            id.ToString(),
            $"Closed fiscal year: {year.YearLabel}",
            cancellationToken: cancellationToken);
    }

    public async Task ReopenFiscalYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var year = await _db.CompanyFiscalYears.FirstOrDefaultAsync(y => y.Id == id, cancellationToken)
            ?? throw new BusinessException("Fiscal year not found.");

        if (year.Status == FiscalYearStatus.Active)
            throw new BusinessException("Fiscal year is already active.");

        var activeYears = await _db.CompanyFiscalYears
            .Where(y => y.CompanyId == year.CompanyId && y.Status == FiscalYearStatus.Active && y.Id != id)
            .ToListAsync(cancellationToken);
        foreach (var ay in activeYears)
            ay.Status = FiscalYearStatus.Closed;

        year.Status = FiscalYearStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "ReopenFiscalYear",
            nameof(CompanyFiscalYear),
            id.ToString(),
            $"Reopened fiscal year: {year.YearLabel}",
            cancellationToken: cancellationToken);
    }
}

