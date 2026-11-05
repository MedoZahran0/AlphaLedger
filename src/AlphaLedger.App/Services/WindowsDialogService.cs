using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Globalization;
using AlphaLedger.App.Localization;
using Microsoft.Win32;

namespace AlphaLedger.App.Services;

public sealed class WindowsDialogService
{
    public void Info(string message, string? title = null)
        => Show(message, title ?? LocalizationService.Instance.GetString("DialogInfoTitle"), MessageBoxImage.Information, MessageBoxButton.OK);

    public void Warning(string message, string? title = null)
        => Show(message, title ?? LocalizationService.Instance.GetString("DialogWarningTitle"), MessageBoxImage.Warning, MessageBoxButton.OK);

    public void Error(string message, string? title = null)
        => Show(message, title ?? LocalizationService.Instance.GetString("DialogErrorTitle"), MessageBoxImage.Error, MessageBoxButton.OK);

    public bool Confirm(string message, string? title = null)
    {
        var result = MessageBox.Show(
            LocalizationService.Instance.TranslateMessage(message),
            LocalizationService.Instance.TranslateMessage(title ?? LocalizationService.Instance.GetString("DialogConfirmTitle")),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No,
            GetOptions());
        return result == MessageBoxResult.Yes;
    }

    public string? SaveExcel(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Instance.GetString("DialogSaveExcelTitle"),
            Filter = LocalizationService.Instance.GetString("DialogSaveExcelFilter"),
            FileName = defaultName,
            AddExtension = true,
            DefaultExt = ".xlsx"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void PrintCollectionReceipt(
        string companyName,
        string feeDescription,
        decimal amount,
        string currency,
        DateOnly paymentDate,
        string paymentMethod,
        string? receiptNumber)
    {
        var localization = LocalizationService.Instance;
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
            return;

        var title = localization.GetString("ReceiptTitle");
        var document = new FlowDocument
        {
            PagePadding = new Thickness(48),
            FlowDirection = localization.GetFlowDirection(),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 14
        };

        var heading = new Paragraph(new Run(title))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        };
        document.Blocks.Add(heading);
        AddReceiptLine(document, "ReceiptNumber", receiptNumber ?? localization.GetString("CommonNotAvailable"));
        AddReceiptLine(document, "ReceiptCompany", companyName);
        AddReceiptLine(document, "ReceiptService", feeDescription);
        AddReceiptLine(document, "ReceiptDate", paymentDate.ToString("D", localization.CurrentCulture));
        AddReceiptLine(document, "ReceiptPaymentMethod", paymentMethod);
        AddReceiptLine(document, "ReceiptAmount", string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", amount, currency));
        document.Blocks.Add(new Paragraph(new Run(localization.GetString("ReceiptManualRecordNotice")))
        {
            FontSize = 10,
            Margin = new Thickness(0, 24, 0, 0)
        });

        printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    private static void Show(string message, string title, MessageBoxImage image, MessageBoxButton buttons)
    {
        var localization = LocalizationService.Instance;
        MessageBox.Show(
            localization.TranslateMessage(message),
            localization.TranslateMessage(title),
            buttons,
            image,
            MessageBoxResult.OK,
            GetOptions());
    }

    private static MessageBoxOptions GetOptions()
    {
        return LocalizationService.Instance.GetFlowDirection() == FlowDirection.RightToLeft
            ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
            : MessageBoxOptions.None;
    }

    private static void AddReceiptLine(FlowDocument document, string key, string value)
    {
        var label = LocalizationService.Instance.GetString(key);
        document.Blocks.Add(new Paragraph(new Run($"{label}: {value}"))
        {
            Margin = new Thickness(0, 6, 0, 6)
        });
    }
}
