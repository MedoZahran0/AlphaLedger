using System.Globalization;
using AlphaLedger.Core.Models;

namespace AlphaLedger.Core.Abstractions;

public interface ICompanyExcelExporter
{
    Task ExportAsync(IReadOnlyList<CompanyListItem> companies, string filePath, CancellationToken cancellationToken = default);
    Task ExportAsync(
        IReadOnlyList<CompanyListItem> companies,
        string filePath,
        IReadOnlyList<string> headers,
        string worksheetName,
        string activeStatus,
        string archivedStatus,
        CultureInfo culture,
        CancellationToken cancellationToken = default);
}
