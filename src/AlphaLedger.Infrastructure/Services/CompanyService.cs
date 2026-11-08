using System.Text.RegularExpressions;
using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Enums;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using AlphaLedger.Core.Services;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlphaLedger.Infrastructure.Services;

public sealed class CompanyService : ICompanyService
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly AlphaLedgerDbContext _db;
    private readonly ICompanyFolderService _folders;
    private readonly ISettingsService _settings;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(
        AlphaLedgerDbContext db,
        ICompanyFolderService folders,
        ISettingsService settings,
        IAuditService audit,
        IClock clock,
        IFileSystem fileSystem,
        ILogger<CompanyService> logger)
    {
        _db = db;
        _folders = folders;
        _settings = settings;
        _audit = audit;
        _clock = clock;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CompanyListItem>> GetCompaniesAsync(CompanyFilter filter, CancellationToken cancellationToken = default)
    {
        var today = _clock.TodayLocal;
        var query = _db.Companies.AsNoTracking().AsQueryable();

        if (!filter.IncludeArchived)
        {
            query = query.Where(c => !c.IsArchived);
        }

        if (filter.AssignedEmployeeId.HasValue)
        {
            query = query.Where(c => c.AssignedEmployeeId == filter.AssignedEmployeeId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Activity))
        {
            query = query.Where(c => c.BusinessActivity != null && c.BusinessActivity.Contains(filter.Activity));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var text = filter.SearchText.Trim();
            query = query.Where(c =>
                c.DisplayName.Contains(text) ||
                c.LegalNameAr.Contains(text) ||
                c.Code.Contains(text) ||
                (c.TaxRegistrationNumber != null && c.TaxRegistrationNumber.Contains(text)) ||
                (c.TaxFileNumber != null && c.TaxFileNumber.Contains(text)) ||
                (c.EnglishName != null && c.EnglishName.Contains(text)));
        }

        var rows = await query
            .Select(c => new CompanyListItem
            {
                Id = c.Id,
                Code = c.Code,
                DisplayName = c.DisplayName,
                LegalNameAr = c.LegalNameAr,
                TaxRegistrationNumber = c.TaxRegistrationNumber,
                TaxFileNumber = c.TaxFileNumber,
                TaxOffice = c.TaxOffice,
                BusinessActivity = c.BusinessActivity,
                AssignedEmployeeName = c.AssignedEmployee != null ? c.AssignedEmployee.FullName : null,
                ClientSince = c.ClientSince,
                OperationsCount = c.Operations.Count,
                TotalAgreedFees = c.Fees.Where(f => f.Status != FeeStatus.Cancelled).Sum(f => (decimal?)f.Amount) ?? 0,
                TotalCollections = c.Collections.Where(x => !x.IsReversed).Sum(x => (decimal?)x.AmountReceived) ?? 0,
                OutstandingOperations = c.Operations.Count(o =>
                    o.Status != OperationStatus.Completed &&
                    o.Status != OperationStatus.Cancelled &&
                    o.DueDate != null &&
                    o.DueDate < today),
                NextTaxDeadline = c.TaxReturns
                    .Where(t => t.FilingStatus != TaxReturnFilingStatus.Submitted && t.OfficialReceiptNumber == null)
                    .OrderBy(t => t.FilingDueDate)
                    .Select(t => (DateOnly?)t.FilingDueDate)
                    .FirstOrDefault(),
                IsArchived = c.IsArchived,
                ArchiveStartYear = c.ArchiveStartYear,
                DocumentStoragePath = c.DocumentStoragePath
            })
            .OrderBy(c => c.DisplayName)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.RemainingFees = row.TotalAgreedFees - row.TotalCollections;
            var check = _folders.CheckFolder(row.DocumentStoragePath);
            row.FolderMissing = !check.Exists;
        }

        return rows;
    }

    public Task<Company?> GetCompanyAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Companies
            .Include(c => c.AssignedEmployee)
            .Include(c => c.FiscalYears.OrderByDescending(y => y.StartDate))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CompanyFiscalYear>> GetFiscalYearsAsync(Guid companyId, CancellationToken cancellationToken = default)
        => await _db.CompanyFiscalYears
            .AsNoTracking()
            .Where(y => y.CompanyId == companyId)
            .OrderByDescending(y => y.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DuplicateMatch>> FindLikelyDuplicatesAsync(CompanyEditModel model, CancellationToken cancellationToken = default)
    {
        var companies = await _db.Companies.AsNoTracking().ToListAsync(cancellationToken);
        return DuplicateCompanyDetector.Find(companies, model);
    }

    public async Task<Company> CreateCompanyAsync(CompanyEditModel model, CancellationToken cancellationToken = default)
    {
        Validate(model);
        await EnsureNoUnexpectedDuplicates(model, cancellationToken);

        var documentRoot = await RequireDocumentRoot(cancellationToken);
        var now = _clock.UtcNow;
        var employeeId = await ResolveEmployeeAsync(model, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var codes = await _db.Companies.Select(c => c.Code).ToListAsync(cancellationToken);
            var company = new Company
            {
                Id = Guid.NewGuid(),
                Code = CompanyCodeGenerator.NextCode(codes),
                LegalNameAr = model.LegalNameAr.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.LegalNameAr.Trim() : model.DisplayName.Trim(),
                EnglishName = NullIfEmpty(model.EnglishName),
                TaxRegistrationNumber = NullIfEmpty(model.TaxRegistrationNumber),
                TaxFileNumber = NullIfEmpty(model.TaxFileNumber),
                TaxOffice = NullIfEmpty(model.TaxOffice),
                BusinessActivity = NullIfEmpty(model.BusinessActivity),
                LegalForm = NullIfEmpty(model.LegalForm),
                Address = NullIfEmpty(model.Address),
                PhoneNumbers = NullIfEmpty(model.PhoneNumbers),
                Email = NullIfEmpty(model.Email),
                ContactPerson = NullIfEmpty(model.ContactPerson),
                ContactDetails = NullIfEmpty(model.ContactDetails),
                ClientSince = model.ClientSince ?? _clock.TodayLocal,
                RecordCreatedAtUtc = now,
                AssignedEmployeeId = employeeId,
                FeeAgreementDetails = NullIfEmpty(model.FeeAgreementDetails),
                InternalNotes = NullIfEmpty(model.InternalNotes),
                IsArchived = false,
                ArchiveStartYear = model.ArchiveStartYear ?? _clock.TodayLocal.Year,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            company.DocumentStoragePath = _folders.BuildCompanyFolderPath(
                documentRoot,
                company.ArchiveStartYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                company);

            _db.Companies.Add(company);
            if (model.CreateCurrentFiscalYear)
            {
                var year = BuildDefaultFiscalYear(company.Id, model);
                _db.CompanyFiscalYears.Add(year);
            }

            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await _audit.WriteAsync("Create", nameof(Company), company.Id.ToString(), $"تم إنشاء الشركة {company.DisplayName} بالكود {company.Code}", cancellationToken: cancellationToken);
            _logger.LogInformation("Created company {CompanyId} with code {Code}", company.Id, company.Code);
            return company;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Company> UpdateCompanyAsync(CompanyEditModel model, CancellationToken cancellationToken = default)
    {
        if (!model.Id.HasValue)
        {
            throw new BusinessException("معرّف الشركة غير موجود.");
        }

        Validate(model);
        await EnsureNoUnexpectedDuplicates(model, cancellationToken);

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == model.Id.Value, cancellationToken)
                      ?? throw new BusinessException("الشركة غير موجودة.");

        var previousName = company.DisplayName;
        var employeeId = await ResolveEmployeeAsync(model, cancellationToken);

        company.LegalNameAr = model.LegalNameAr.Trim();
        company.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.LegalNameAr.Trim() : model.DisplayName.Trim();
        company.EnglishName = NullIfEmpty(model.EnglishName);
        company.TaxRegistrationNumber = NullIfEmpty(model.TaxRegistrationNumber);
        company.TaxFileNumber = NullIfEmpty(model.TaxFileNumber);
        company.TaxOffice = NullIfEmpty(model.TaxOffice);
        company.BusinessActivity = NullIfEmpty(model.BusinessActivity);
        company.LegalForm = NullIfEmpty(model.LegalForm);
        company.Address = NullIfEmpty(model.Address);
        company.PhoneNumbers = NullIfEmpty(model.PhoneNumbers);
        company.Email = NullIfEmpty(model.Email);
        company.ContactPerson = NullIfEmpty(model.ContactPerson);
        company.ContactDetails = NullIfEmpty(model.ContactDetails);
        company.ClientSince = model.ClientSince;
        company.AssignedEmployeeId = employeeId;
        company.FeeAgreementDetails = NullIfEmpty(model.FeeAgreementDetails);
        company.InternalNotes = NullIfEmpty(model.InternalNotes);
        company.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(
            "Update",
            nameof(Company),
            company.Id.ToString(),
            previousName == company.DisplayName
                ? $"تم تعديل بيانات الشركة {company.DisplayName}"
                : $"تم تغيير اسم الشركة من «{previousName}» إلى «{company.DisplayName}» دون تغيير المعرّف الداخلي",
            cancellationToken: cancellationToken);

        return company;
    }

    public async Task ArchiveCompanyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await RequireCompany(id, cancellationToken);
        company.IsArchived = true;
        company.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Archive", nameof(Company), company.Id.ToString(), $"تم أرشفة الشركة {company.DisplayName}", cancellationToken: cancellationToken);
    }

    public async Task RestoreCompanyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await RequireCompany(id, cancellationToken);
        company.IsArchived = false;
        company.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Restore", nameof(Company), company.Id.ToString(), $"تم استرجاع الشركة {company.DisplayName}", cancellationToken: cancellationToken);
    }

    public async Task DeleteCompanyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await RequireCompany(id, cancellationToken);
        var displayName = company.DisplayName;
        var code = company.Code;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Attachment metadata has no FK to Companies (the table is created outside
            // EF Core), so remove its rows explicitly. Files on disk are left untouched.
            await DeleteCompanyAttachmentsAsync(company.Id, cancellationToken);

            // Related fiscal years, operations, fees, collections, tax returns and
            // portal accounts are removed by the database cascade.
            _db.Companies.Remove(company);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        await _audit.WriteAsync(
            "Delete",
            nameof(Company),
            id.ToString(),
            $"تم حذف الشركة {displayName} بالكود {code} من البرنامج. لم يتم حذف ملفاتها من القرص.",
            cancellationToken: cancellationToken);
    }

    private async Task DeleteCompanyAttachmentsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='ArchiveAttachments';";
                var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
                if (!exists)
                {
                    return;
                }
            }

            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM \"ArchiveAttachments\" WHERE \"CompanyId\" = @companyId;";
            var parameter = delete.CreateParameter();
            parameter.ParameterName = "@companyId";
            parameter.Value = companyId.ToString();
            delete.Parameters.Add(parameter);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<CompanyFiscalYear> AddFiscalYearAsync(Guid companyId, FiscalYearEditModel model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.YearLabel))
        {
            throw new BusinessException("أدخل تسمية السنة المالية.");
        }

        if (model.EndDate < model.StartDate)
        {
            throw new BusinessException("تاريخ نهاية السنة المالية يجب أن يكون بعد تاريخ البداية.");
        }

        var company = await RequireCompany(companyId, cancellationToken);
        var exists = await _db.CompanyFiscalYears.AnyAsync(
            y => y.CompanyId == companyId && y.YearLabel == model.YearLabel.Trim(),
            cancellationToken);
        if (exists)
        {
            throw new BusinessException("هذه السنة المالية مسجلة مسبقاً لهذه الشركة.");
        }

        var year = new CompanyFiscalYear
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            YearLabel = model.YearLabel.Trim(),
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Status = model.Status,
            Notes = NullIfEmpty(model.Notes),
            CreatedAtUtc = _clock.UtcNow
        };

        _db.CompanyFiscalYears.Add(year);
        await _db.SaveChangesAsync(cancellationToken);

        var documentRoot = await RequireDocumentRoot(cancellationToken);
        year.FolderPath = _folders.EnsureFiscalYearFolder(documentRoot, company, year);
        if (string.IsNullOrWhiteSpace(company.DocumentStoragePath))
        {
            company.DocumentStoragePath = _folders.EnsureCurrentYearCompanyFolder(documentRoot, company);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync("Create", nameof(CompanyFiscalYear), year.Id.ToString(), $"تمت إضافة السنة {year.YearLabel} للشركة {company.DisplayName}", cancellationToken: cancellationToken);
        return year;
    }

    public async Task<IReadOnlyList<Employee>> GetActiveEmployeesAsync(CancellationToken cancellationToken = default)
        => await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.FullName)
            .ToListAsync(cancellationToken);

    public async Task<Employee> QuickAddEmployeeAsync(string fullName, string? jobTitle = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new BusinessException("أدخل اسم الموظف.");
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            JobTitle = NullIfEmpty(jobTitle),
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(cancellationToken);
        return employee;
    }

    public async Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                       ?? throw new BusinessException("الموظف غير موجود.");

        var assignedCompanies = await _db.Companies
            .Where(company => company.AssignedEmployeeId == id)
            .ToListAsync(cancellationToken);
        foreach (var company in assignedCompanies)
        {
            company.AssignedEmployeeId = null;
            company.UpdatedAtUtc = _clock.UtcNow;
        }

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(
            "Delete",
            nameof(Employee),
            id.ToString(),
            $"تم حذف الموظف {employee.FullName} وإلغاء إسناد الشركات المرتبطة به.",
            cancellationToken: cancellationToken);
    }

    public async Task RelocateCompanyFolderAsync(Guid companyId, string newPath, CancellationToken cancellationToken = default)
    {
        var company = await RequireCompany(companyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(newPath))
        {
            throw new BusinessException("حدد المسار الجديد لمجلد الشركة.");
        }

        if (!_fileSystem.DirectoryExists(newPath))
        {
            throw new FolderAccessException("المجلد المحدد غير موجود.", newPath);
        }

        if (!_fileSystem.IsPathWritable(newPath))
        {
            throw new FolderAccessException("المجلد المحدد غير قابل للكتابة.", newPath);
        }

        var oldPath = company.DocumentStoragePath;
        company.DocumentStoragePath = newPath;
        company.UpdatedAtUtc = _clock.UtcNow;

        var years = await _db.CompanyFiscalYears.Where(y => y.CompanyId == company.Id).ToListAsync(cancellationToken);
        foreach (var year in years)
        {
            var candidate = Path.Combine(newPath, WindowsPathSafetySafe(year.YearLabel));
            year.FolderPath = _fileSystem.DirectoryExists(candidate) ? candidate : newPath;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(
            "RelocateFolder",
            nameof(Company),
            company.Id.ToString(),
            $"تم تحديث مسار مجلد الشركة {company.DisplayName}. المسار السابق: {oldPath}. المسار الجديد: {newPath}. لم يُحذف المجلد القديم تلقائياً.",
            cancellationToken: cancellationToken);
    }

    private static string WindowsPathSafetySafe(string yearLabel)
        => Core.Security.WindowsPathSafety.SanitizeFolderName(yearLabel);

    private async Task<Company> RequireCompany(Guid id, CancellationToken cancellationToken)
        => await _db.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
           ?? throw new BusinessException("الشركة غير موجودة.");

    private async Task<string> RequireDocumentRoot(CancellationToken cancellationToken)
    {
        var root = await _settings.GetDocumentRootAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new BusinessException("حدد أولاً مجلد تخزين مستندات الشركات من الإعدادات.");
        }

        _folders.EnsureDocumentRoot(root);
        return root;
    }

    private async Task EnsureNoUnexpectedDuplicates(CompanyEditModel model, CancellationToken cancellationToken)
    {
        if (model.IgnoreDuplicateWarning)
        {
            return;
        }

        var matches = await FindLikelyDuplicatesAsync(model, cancellationToken);
        if (matches.Count > 0)
        {
            var details = string.Join("؛ ", matches.Select(m => $"{m.DisplayName} ({m.Code}) - {m.Reason}"));
            throw new DuplicateCompanyException($"يبدو أن هذه الشركة مسجلة مسبقاً: {details}");
        }
    }

    private async Task<Guid?> ResolveEmployeeAsync(CompanyEditModel model, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(model.NewEmployeeName))
        {
            var created = await QuickAddEmployeeAsync(model.NewEmployeeName, cancellationToken: cancellationToken);
            return created.Id;
        }

        return model.AssignedEmployeeId;
    }

    private CompanyFiscalYear BuildDefaultFiscalYear(Guid companyId, CompanyEditModel model)
    {
        var start = model.FiscalYearStart ?? new DateOnly(_clock.TodayLocal.Year, 1, 1);
        var end = model.FiscalYearEnd ?? new DateOnly(_clock.TodayLocal.Year, 12, 31);
        return new CompanyFiscalYear
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            YearLabel = start.Year.ToString(),
            StartDate = start,
            EndDate = end,
            Status = FiscalYearStatus.Active,
            CreatedAtUtc = _clock.UtcNow
        };
    }

    private static void Validate(CompanyEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.LegalNameAr))
        {
            throw new BusinessException("الاسم القانوني بالعربية مطلوب.");
        }

        if (model.ArchiveStartYear is < 1900 or > 9999)
        {
            throw new BusinessException("حدد سنة أرشيف صحيحة بين 1900 و9999.");
        }

        if (!string.IsNullOrWhiteSpace(model.Email) && !EmailPattern.IsMatch(model.Email.Trim()))
        {
            throw new BusinessException("صيغة البريد الإلكتروني غير صحيحة.");
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DuplicateCompanyException : BusinessException
{
    public DuplicateCompanyException(string message) : base(message)
    {
    }
}
