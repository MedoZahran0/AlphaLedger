using AlphaLedger.Core.Abstractions;

namespace AlphaLedger.Infrastructure.Storage;

public sealed class LocalAppPaths : IAppPaths
{
    public LocalAppPaths(string? rootOverride = null)
    {
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger");
        var markerPath = Path.Combine(localRoot, "data-root.txt");
        var selectedRoot = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : null;
        var root = rootOverride ?? (string.IsNullOrWhiteSpace(selectedRoot) ? localRoot : selectedRoot);

        ApplicationRoot = root;
        IsPortableMode = !string.IsNullOrEmpty(rootOverride) || !string.IsNullOrWhiteSpace(selectedRoot);

        AppDirectory = Path.Combine(root, "App");
        var systemRoot = IsPortableMode ? Path.Combine(root, "System") : root;
        DataDirectory = Path.Combine(systemRoot, "Data");
        DocumentsDirectory = Path.Combine(root, "Archive");
        BackupsDirectory = Path.Combine(systemRoot, "Backups");
        LogsDirectory = Path.Combine(systemRoot, IsPortableMode ? "Logs" : Path.Combine("Data", "logs"));
        ConfigDirectory = Path.Combine(systemRoot, IsPortableMode ? "Config" : Path.Combine("Data", "config"));
        DatabasePath = Path.Combine(DataDirectory, "alpha-ledger.db");
    }

    public string ApplicationRoot { get; }
    public string AppDirectory { get; }
    public string DataDirectory { get; }
    public string DocumentsDirectory { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string DatabasePath { get; }
    public string ConfigDirectory { get; }
    public bool IsPortableMode { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DocumentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
    }
}
