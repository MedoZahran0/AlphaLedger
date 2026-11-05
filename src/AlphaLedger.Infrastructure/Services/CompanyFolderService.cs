using System.Globalization;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Core.Models;
using AlphaLedger.Core.Security;

namespace AlphaLedger.Infrastructure.Services;

/// <summary>
/// Builds the on-disk archive layout:
/// Archive\&lt;year&gt;\&lt;CompanyName_Code&gt;\...
/// Year folders are created automatically and company folders start empty.
/// </summary>
public sealed class CompanyFolderService : ICompanyFolderService
{
    private static readonly string[] StandardCompanyFolders =
    [
        "01 - مستندات الشركة العامة",
        "02 - السجل التجاري والبطاقة الضريبية",
        "03 - إقرارات القيمة المضافة",
        "04 - الخصم والتحصيل",
        "05 - ضريبة الدخل",
        "06 - المرتبات وكسب العمل",
        "07 - الفاتورة الإلكترونية",
        "08 - الفواتير والمصروفات",
        "09 - العقود والاتفاقيات",
        "10 - الأتعاب وإيصالات التحصيل",
        "11 - المراسلات والخطابات",
        "12 - مستندات أخرى",
        "99 - المحذوفات"
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    public CompanyFolderService(IFileSystem fileSystem, IClock clock)
    {
        _fileSystem = fileSystem;
        _clock = clock;
    }

    public string CurrentYearLabel => _clock.TodayLocal.Year.ToString(CultureInfo.InvariantCulture);

    public string BuildCompanyFolderName(string displayOrLegalName, string companyCode)
    {
        var safeName = WindowsPathSafety.SanitizeFolderName(displayOrLegalName);
        var safeCode = WindowsPathSafety.SanitizeFolderName(companyCode, "C000000");
        return $"{safeName}_{safeCode}";
    }

    public string GetYearFolderPath(string documentRoot, string yearLabel)
        => Path.Combine(documentRoot, NormalizeYearLabel(yearLabel));

    public string BuildCompanyFolderPath(string documentRoot, string yearLabel, Company company)
        => BuildCompanyFolderPath(documentRoot, yearLabel, company.DisplayName, company.Code);

    public string BuildCompanyFolderPath(string documentRoot, string yearLabel, string displayOrLegalName, string companyCode)
        => Path.Combine(GetYearFolderPath(documentRoot, yearLabel), BuildCompanyFolderName(displayOrLegalName, companyCode));

    public void EnsureDocumentRoot(string documentRoot)
    {
        if (string.IsNullOrWhiteSpace(documentRoot))
        {
            throw new FolderAccessException("لم يتم تحديد مجلد تخزين مستندات الشركات.");
        }

        try
        {
            _fileSystem.CreateDirectory(documentRoot);
        }
        catch (Exception ex)
        {
            throw new FolderAccessException("تعذر إنشاء مجلد تخزين المستندات. تأكد أن المسار صحيح وأن لديك صلاحية الكتابة.", documentRoot, ex);
        }

        if (!_fileSystem.IsPathWritable(documentRoot))
        {
            throw new FolderAccessException("المجلد المحدد غير قابل للكتابة. اختر مجلداً آخر أو راجع صلاحيات ويندوز.", documentRoot);
        }
    }

    public string EnsureYearFolder(string documentRoot, string yearLabel)
    {
        EnsureDocumentRoot(documentRoot);
        var path = GetYearFolderPath(documentRoot, yearLabel);
        CreateDirectorySafe(path);
        return path;
    }

    public string EnsureCompanyFolder(string documentRoot, string yearLabel, Company company)
    {
        var yearPath = EnsureYearFolder(documentRoot, yearLabel);
        var path = Path.Combine(yearPath, BuildCompanyFolderName(company.DisplayName, company.Code));
        CreateDirectorySafe(path);
        foreach (var folderName in StandardCompanyFolders)
        {
            CreateDirectorySafe(Path.Combine(path, folderName));
        }
        return path;
    }

    public string EnsureCurrentYearCompanyFolder(string documentRoot, Company company)
        => EnsureCompanyFolder(documentRoot, CurrentYearLabel, company);

    public string EnsureFiscalYearFolder(string documentRoot, Company company, CompanyFiscalYear fiscalYear)
        => EnsureCompanyFolder(documentRoot, fiscalYear.YearLabel, company);

    public FolderCheckResult CheckFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new FolderCheckResult
            {
                Exists = false,
                IsAccessible = false,
                Path = string.Empty,
                Warning = "لم يُسجل مسار مجلد لهذه الشركة."
            };
        }

        try
        {
            var exists = _fileSystem.DirectoryExists(path);
            return new FolderCheckResult
            {
                Exists = exists,
                IsAccessible = exists,
                Path = path,
                Warning = exists ? null : "مجلد الشركة غير موجود على القرص. يمكنك تحديد مكانه من جديد."
            };
        }
        catch (Exception)
        {
            return new FolderCheckResult
            {
                Exists = false,
                IsAccessible = false,
                Path = path,
                Warning = "تعذر الوصول إلى مجلد الشركة. تحقق من القرص والصلاحيات."
            };
        }
    }

    private string NormalizeYearLabel(string yearLabel)
        => WindowsPathSafety.SanitizeFolderName(
            string.IsNullOrWhiteSpace(yearLabel) ? CurrentYearLabel : yearLabel,
            CurrentYearLabel);

    private void CreateDirectorySafe(string path)
    {
        try
        {
            _fileSystem.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            throw new FolderAccessException($"تعذر إنشاء المجلد:\n{path}", path, ex);
        }
    }
}
