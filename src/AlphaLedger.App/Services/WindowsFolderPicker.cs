using System.IO;
using Microsoft.Win32;

namespace AlphaLedger.App.Services;

public sealed class WindowsFolderPicker
{
    public string? PickFolder(string title, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
