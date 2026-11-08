using System.Diagnostics;
using System.Globalization;
using System.Windows;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Views;

namespace AlphaLedger.App.Services;

public sealed class ApplicationUpdateService(GitHubUpdateService updater)
{
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    public async Task<UpdateCheckOutcome> CheckForUpdatesAsync(
        Window owner,
        bool showUpToDateMessage = false,
        CancellationToken cancellationToken = default,
        bool showCheckingProgress = true)
    {
        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            return await CheckForUpdatesCoreAsync(
                owner, showUpToDateMessage, cancellationToken, showCheckingProgress);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<UpdateCheckOutcome> CheckForUpdatesCoreAsync(
        Window owner,
        bool showUpToDateMessage,
        CancellationToken cancellationToken,
        bool showCheckingProgress)
    {
        var localization = LocalizationService.Instance;
        UpdateProgressWindow? progressWindow = null;
        if (showCheckingProgress)
        {
            progressWindow = new UpdateProgressWindow { Owner = owner };
            progressWindow.SetChecking();
            progressWindow.Show();
        }

        GitHubUpdateService.ReleaseUpdate? update;
        try
        {
            update = await updater.CheckForUpdateAsync(cancellationToken);
        }
        finally
        {
            progressWindow?.Close();
        }

        if (update is null)
        {
            if (showUpToDateMessage)
            {
                MessageBox.Show(
                    owner,
                    localization.GetString("SettingsUpdateNoUpdates"),
                    localization.GetString("UpdateProgressTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return UpdateCheckOutcome.UpToDate;
        }

        var answer = MessageBox.Show(
            owner,
            string.Format(localization.CurrentCulture,
                localization.GetString("UpdateAvailableMessage"), update.TagName),
            localization.GetString("UpdateAvailableTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            return UpdateCheckOutcome.UpdateDeclined;
        }

        var downloadWindow = new UpdateProgressWindow { Owner = owner };
        downloadWindow.SetDownloading();
        downloadWindow.Show();
        GitHubUpdateService.DownloadedUpdate downloadedUpdate;
        try
        {
            var progress = new Progress<GitHubUpdateService.DownloadProgress>(downloadWindow.ReportProgress);
            downloadedUpdate = await updater.DownloadUpdateAsync(update, progress, cancellationToken);
        }
        finally
        {
            downloadWindow.Close();
        }

        var restartAnswer = MessageBox.Show(
            owner,
            string.Format(localization.CurrentCulture,
                localization.GetString("UpdateReadyRestartMessage"), update.TagName),
            localization.GetString("UpdateAvailableTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (restartAnswer != MessageBoxResult.Yes)
        {
            return UpdateCheckOutcome.UpdateDownloaded;
        }

        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current application executable path could not be determined.");
        var restartInfo = CreateFullUpdaterStartInfo(downloadedUpdate.UpdatePath, update.Digest, currentExecutable);
        var updaterProcess = Process.Start(restartInfo);
        if (updaterProcess is null)
        {
            throw new InvalidOperationException("The update helper process could not be started.");
        }

        Application.Current.Shutdown();
        return UpdateCheckOutcome.Restarting;
    }

    private static ProcessStartInfo CreateFullUpdaterStartInfo(
        string updatePath,
        string expectedDigest,
        string currentExecutable)
    {
        var startInfo = new ProcessStartInfo(updatePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(currentExecutable);
        startInfo.ArgumentList.Add(updatePath);
        startInfo.ArgumentList.Add(expectedDigest);
        return startInfo;
    }

}

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateDeclined,
    UpdateDownloaded,
    Restarting
}
