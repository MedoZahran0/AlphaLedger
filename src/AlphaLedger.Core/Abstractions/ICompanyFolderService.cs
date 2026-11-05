using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;

namespace AlphaLedger.Core.Abstractions;

public interface ICompanyFolderService
{
    /// <summary>The current calendar year used for the archive year folder (for example "2026").</summary>
    string CurrentYearLabel { get; }

    string BuildCompanyFolderName(string displayOrLegalName, string companyCode);
    string GetYearFolderPath(string documentRoot, string yearLabel);
    string BuildCompanyFolderPath(string documentRoot, string yearLabel, Company company);
    string BuildCompanyFolderPath(string documentRoot, string yearLabel, string displayOrLegalName, string companyCode);
    void EnsureDocumentRoot(string documentRoot);

    /// <summary>Creates the year folder directly under the document root (for example Archive\2026).</summary>
    string EnsureYearFolder(string documentRoot, string yearLabel);

    /// <summary>Creates a company folder and its standard archive folders inside the requested year.</summary>
    string EnsureCompanyFolder(string documentRoot, string yearLabel, Company company);

    /// <summary>Creates the company folder inside the current calendar year folder.</summary>
    string EnsureCurrentYearCompanyFolder(string documentRoot, Company company);

    /// <summary>Creates the company folder for a fiscal year (Archive\2026\Company_C000001).</summary>
    string EnsureFiscalYearFolder(string documentRoot, Company company, CompanyFiscalYear fiscalYear);

    FolderCheckResult CheckFolder(string? path);
}
