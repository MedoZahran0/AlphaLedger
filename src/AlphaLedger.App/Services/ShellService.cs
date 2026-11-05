using System.Diagnostics;
using System.IO;
using AlphaLedger.App.Localization;
using AlphaLedger.Core.Exceptions;

namespace AlphaLedger.App.Services;

public static class ShellService
{
    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new FolderAccessException(LocalizationService.Instance.GetString("ShellFolderMissing"), path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }
}
