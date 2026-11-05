using System.Globalization;
using System.Windows;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;

namespace AlphaLedger.App.Views;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void ReportProgress(GitHubUpdateService.DownloadProgress progress)
    {
        if (progress.TotalBytes is not > 0)
        {
            DownloadProgressBar.IsIndeterminate = true;
            ProgressDetails.Text = LocalizationService.Instance.GetString("UpdateProgressPreparing");
            return;
        }

        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = Math.Clamp(
            progress.BytesReceived * 100d / progress.TotalBytes.Value, 0, 100);
        ProgressDetails.Text = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Instance.GetString("UpdateProgressDownloaded"),
            FormatSize(progress.BytesReceived),
            FormatSize(progress.TotalBytes.Value));
    }

    private static string FormatSize(long bytes) =>
        string.Format(CultureInfo.CurrentCulture, "{0:N1} MB", bytes / 1024d / 1024d);
}
