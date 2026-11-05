using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Models;
using ClosedXML.Excel;
using System.Globalization;

namespace AlphaLedger.Infrastructure.Export;

public sealed class CompanyExcelExporter : ICompanyExcelExporter
{
    public Task ExportAsync(IReadOnlyList<CompanyListItem> companies, string filePath, CancellationToken cancellationToken = default)
        => ExportAsync(
            companies,
            filePath,
            ["الكود", "اسم الشركة", "الاسم القانوني", "الرقم الضريبي", "النشاط", "الموظف المسؤول", "عدد العمليات", "إجمالي الأتعاب", "إجمالي التحصيل", "المتبقي", "عمليات متأخرة", "أقرب موعد إقرار", "الحالة"],
            "الشركات",
            "نشط",
            "مؤرشف",
            CultureInfo.GetCultureInfo("ar-EG"),
            cancellationToken);

    public Task ExportAsync(
        IReadOnlyList<CompanyListItem> companies,
        string filePath,
        IReadOnlyList<string> headers,
        string worksheetName,
        string activeStatus,
        string archivedStatus,
        CultureInfo culture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook();
        var isRightToLeft = culture.TextInfo.IsRightToLeft;
        var sheet = workbook.Worksheets.Add(worksheetName);
        sheet.RightToLeft = isRightToLeft;

        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        var header = sheet.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("1B365D");
        header.Style.Font.FontColor = XLColor.White;

        var row = 2;
        foreach (var company in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheet.Cell(row, 1).Value = company.Code;
            sheet.Cell(row, 2).Value = company.DisplayName;
            sheet.Cell(row, 3).Value = company.LegalNameAr;
            sheet.Cell(row, 4).Value = company.TaxRegistrationNumber;
            sheet.Cell(row, 5).Value = company.BusinessActivity;
            sheet.Cell(row, 6).Value = company.AssignedEmployeeName;
            sheet.Cell(row, 7).Value = company.OperationsCount;
            sheet.Cell(row, 8).Value = company.TotalAgreedFees;
            sheet.Cell(row, 9).Value = company.TotalCollections;
            sheet.Cell(row, 10).Value = company.RemainingFees;
            sheet.Cell(row, 11).Value = company.OutstandingOperations;
            sheet.Cell(row, 12).Value = company.NextTaxDeadline?.ToString("d", culture);
            sheet.Cell(row, 13).Value = company.IsArchived ? archivedStatus : activeStatus;
            row++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
        return Task.CompletedTask;
    }
}
