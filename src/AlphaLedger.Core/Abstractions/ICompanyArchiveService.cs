namespace AlphaLedger.Core.Abstractions;

public interface ICompanyArchiveService
{
    /// <summary>
    /// Creates the current calendar-year folder and each active company's configured archive
    /// start-year folder. Existing folders and files are never deleted or cleared.
    /// </summary>
    Task EnsureCurrentYearStructureAsync(CancellationToken cancellationToken = default);

    Task<string> GetCompanyArchiveRootAsync(Guid companyId, string companyCode, string? preferredCompanyPath = null, CancellationToken cancellationToken = default);
    Task<string> EnsureCompanyFolderAsync(Guid companyId, string companyCode, string yearLabel, CancellationToken cancellationToken = default);
    Task EnsureCompanyFoldersAsync(Guid companyId, string companyCode, CancellationToken cancellationToken = default);
    Task<int> CopyCompanyFolderStructureToYearAsync(string sourceYearLabel, string destinationYearLabel, CancellationToken cancellationToken = default);
    Task<string> GetCategoryFolderAsync(Guid companyId, string companyCode, int categoryNumber, Guid? fiscalYearId = null, CancellationToken cancellationToken = default);
    Task<string> ImportFileToCategoryAsync(Guid companyId, string companyCode, int categoryNumber, string sourcePath, CancellationToken cancellationToken = default, Guid? fiscalYearId = null);
}
