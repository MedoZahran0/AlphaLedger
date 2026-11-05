using System.Text;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

/// <summary>
/// Owns the archive layout on disk. The archive root contains one folder per calendar year,
/// and each year folder contains one folder per company. Standard company archive folders
/// are created when the company folder is first created.
/// </summary>
public sealed class CompanyArchiveService : ICompanyArchiveService
{
    private static readonly string[] FolderNames =
    {
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
    };

    private readonly ISettingsService _settings;
    private readonly ICompanyService _companies;
    private readonly ICompanyFolderService _folders;
    private readonly IClock _clock;
    private readonly AlphaLedgerDbContext _db;

    public CompanyArchiveService(
        ISettingsService settings,
        ICompanyService companies,
        ICompanyFolderService folders,
        IClock clock,
        AlphaLedgerDbContext db)
    {
        _settings = settings;
        _companies = companies;
        _folders = folders;
        _clock = clock;
        _db = db;
    }

    public async Task EnsureCurrentYearStructureAsync(CancellationToken cancellationToken = default)
    {
        var root = await GetDocumentRootAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        _folders.EnsureYearFolder(root, _folders.CurrentYearLabel);

        var companies = await _db.Companies
            .Where(company => !company.IsArchived)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var company in companies)
        {
            var archiveYear = company.ArchiveStartYear
                ?? GetArchiveYearFromPath(company.DocumentStoragePath, root)
                ?? _clock.TodayLocal.Year;
            if (company.ArchiveStartYear != archiveYear)
            {
                company.ArchiveStartYear = archiveYear;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(company.DocumentStoragePath) ||
                !IsInsideRoot(company.DocumentStoragePath, root))
            {
                continue;
            }

            var archivePath = _folders.EnsureCompanyFolder(
                root,
                archiveYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                company);
            if (!string.Equals(company.DocumentStoragePath, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                company.DocumentStoragePath = archivePath;
                company.UpdatedAtUtc = _clock.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<string> GetCompanyArchiveRootAsync(
        Guid companyId,
        string companyCode,
        string? preferredCompanyPath = null,
        CancellationToken cancellationToken = default)
    {
        var root = await GetDocumentRootAsync(cancellationToken);
        var company = await _companies.GetCompanyAsync(companyId, cancellationToken);

        if (company is null)
        {
            return !string.IsNullOrWhiteSpace(preferredCompanyPath)
                ? preferredCompanyPath
                : Path.Combine(root, _folders.CurrentYearLabel, companyCode);
        }

        // Callers that explicitly browse a specific year (for example a fiscal year node)
        // pass the folder path they want to use.
        if (!string.IsNullOrWhiteSpace(preferredCompanyPath))
        {
            Directory.CreateDirectory(preferredCompanyPath);
            return preferredCompanyPath;
        }

        var archiveYear = company.ArchiveStartYear
            ?? GetArchiveYearFromPath(company.DocumentStoragePath, root)
            ?? _clock.TodayLocal.Year;
        if (!string.IsNullOrWhiteSpace(company.DocumentStoragePath) &&
            Directory.Exists(company.DocumentStoragePath))
        {
            if (company.ArchiveStartYear != archiveYear)
            {
                company.ArchiveStartYear = archiveYear;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return company.DocumentStoragePath;
        }

        var currentYearPath = _folders.EnsureCompanyFolder(
            root,
            archiveYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
            company);
        company.ArchiveStartYear = archiveYear;
        company.DocumentStoragePath = currentYearPath;
        await _db.SaveChangesAsync(cancellationToken);
        var legacyRoot = Path.Combine(root, "Companies", companyCode);
        if (!string.Equals(Path.GetFullPath(currentYearPath), Path.GetFullPath(legacyRoot), StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(legacyRoot))
        {
            await MergeLegacyArchiveAsync(legacyRoot, currentYearPath, cancellationToken);
        }

        return currentYearPath;
    }

    public async Task<string> EnsureCompanyFolderAsync(
        Guid companyId,
        string companyCode,
        string yearLabel,
        CancellationToken cancellationToken = default)
    {
        var root = await GetDocumentRootAsync(cancellationToken);
        var company = await _companies.GetCompanyAsync(companyId, cancellationToken);
        if (company is null)
        {
            var fallback = Path.Combine(root, yearLabel, companyCode);
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        return _folders.EnsureCompanyFolder(root, yearLabel, company);
    }

    public async Task EnsureCompanyFoldersAsync(Guid companyId, string companyCode, CancellationToken cancellationToken = default)
    {
        await GetCompanyArchiveRootAsync(companyId, companyCode, cancellationToken: cancellationToken);
    }

    public async Task<string> GetCategoryFolderAsync(
        Guid companyId,
        string companyCode,
        int categoryNumber,
        Guid? fiscalYearId = null,
        CancellationToken cancellationToken = default)
    {
        var companyRoot = await ResolveCompanyRootAsync(companyId, companyCode, fiscalYearId, cancellationToken);
        return GetOrCreateCategoryFolder(companyRoot, categoryNumber);
    }

    public async Task<string> ImportFileToCategoryAsync(
        Guid companyId,
        string companyCode,
        int categoryNumber,
        string sourcePath,
        CancellationToken cancellationToken = default,
        Guid? fiscalYearId = null)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file not found.", sourcePath);
        }

        var companyRoot = await ResolveCompanyRootAsync(companyId, companyCode, fiscalYearId, cancellationToken);
        var targetDir = GetOrCreateCategoryFolder(companyRoot, categoryNumber);
        var dest = Path.Combine(targetDir, Path.GetFileName(sourcePath));
        if (File.Exists(dest))
        {
            var name = Path.GetFileNameWithoutExtension(dest);
            var ext = Path.GetExtension(dest);
            var index = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(targetDir, $"{name} ({index}){ext}");
                index++;
            }
            while (File.Exists(candidate));
            dest = candidate;
        }

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var destination = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        return dest;
    }

    private async Task<string> ResolveCompanyRootAsync(
        Guid companyId,
        string companyCode,
        Guid? fiscalYearId,
        CancellationToken cancellationToken)
    {
        if (fiscalYearId is Guid id)
        {
            var year = await _db.CompanyFiscalYears
                .AsNoTracking()
                .Where(item => item.Id == id && item.CompanyId == companyId)
                .Select(item => new { item.YearLabel, item.FolderPath })
                .FirstOrDefaultAsync(cancellationToken);

            if (year is not null)
            {
                if (!string.IsNullOrWhiteSpace(year.FolderPath))
                {
                    Directory.CreateDirectory(year.FolderPath);
                    return year.FolderPath;
                }

                return await EnsureCompanyFolderAsync(companyId, companyCode, year.YearLabel, cancellationToken);
            }
        }

        return await GetCompanyArchiveRootAsync(companyId, companyCode, cancellationToken: cancellationToken);
    }

    private async Task<string> GetDocumentRootAsync(CancellationToken cancellationToken)
    {
        var root = await _settings.GetDocumentRootAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AlphaLedger_Archive");
        }

        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetOrCreateCategoryFolder(string companyRoot, int categoryNumber)
    {
        Directory.CreateDirectory(companyRoot);
        var prefix = $"{categoryNumber:D2} -";
        var existing = Directory.EnumerateDirectories(companyRoot)
            .FirstOrDefault(directory => Path.GetFileName(directory).StartsWith(prefix, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var name = FolderNames.FirstOrDefault(folder => folder.StartsWith(prefix, StringComparison.Ordinal))
                   ?? $"{categoryNumber:D2} - مستندات أخرى";
        var path = Path.Combine(companyRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int? GetArchiveYearFromPath(string? companyPath, string documentRoot)
    {
        if (string.IsNullOrWhiteSpace(companyPath) || !IsInsideRoot(companyPath, documentRoot))
            return null;

        var year = Path.GetFileName(Path.GetDirectoryName(companyPath));
        return int.TryParse(year, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value is >= 1900 and <= 9999
            ? value
            : null;
    }

    private static bool IsInsideRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root);
            var relative = Path.GetRelativePath(fullRoot, fullPath);
            return relative == "." ||
                   (!Path.IsPathRooted(relative) &&
                    relative != ".." &&
                    !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private async Task MergeLegacyArchiveAsync(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        var markerDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger");
        Directory.CreateDirectory(markerDirectory);
        var source = Path.GetFullPath(sourceRoot);
        var destination = Path.GetFullPath(destinationRoot);
        var markerHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source + "|" + destination)));
        var markerPath = Path.Combine(markerDirectory, $"archive-unified-{markerHash}.done");
        if (File.Exists(markerPath))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".al-restore.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !FilesMatch(file, target))
            {
                target = GetUniqueCopyPath(target);
            }

            if (!File.Exists(target))
            {
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            pathMap[file] = target;
        }

        foreach (var sidecar in Directory.EnumerateFiles(source, "*.al-restore.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldItemPath = sidecar[..^".al-restore.json".Length];
            var targetSidecar = pathMap.TryGetValue(oldItemPath, out var copiedFile)
                ? copiedFile + ".al-restore.json"
                : Path.Combine(destination, Path.GetRelativePath(source, sidecar));
            if (File.Exists(targetSidecar))
            {
                continue;
            }

            var originalPath = await File.ReadAllTextAsync(sidecar, cancellationToken);
            originalPath = originalPath.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(targetSidecar)!);
            await File.WriteAllTextAsync(targetSidecar, originalPath, cancellationToken);
        }

        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        foreach (var (oldPath, newPath) in pathMap)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ArchiveAttachments SET FilePath=@newPath, FileName=@fileName WHERE FilePath=@oldPath;";
            command.Parameters.Add(new SqliteParameter("@oldPath", oldPath));
            command.Parameters.Add(new SqliteParameter("@newPath", newPath));
            command.Parameters.Add(new SqliteParameter("@fileName", Path.GetFileName(newPath)));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await File.WriteAllTextAsync(markerPath, _clock.UtcNow.ToString("O"), cancellationToken);
    }

    private static bool FilesMatch(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        using var firstStream = firstInfo.OpenRead();
        using var secondStream = secondInfo.OpenRead();
        return System.Security.Cryptography.SHA256.HashData(firstStream)
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(secondStream));
    }

    private static string GetUniqueCopyPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} (archive copy {index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
