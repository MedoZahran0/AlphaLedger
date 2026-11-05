namespace AlphaLedger.Core.Abstractions;

public interface IAppPaths
{
    string ApplicationRoot { get; }
    string AppDirectory { get; }
    string DataDirectory { get; }
    string DocumentsDirectory { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string DatabasePath { get; }
    string ConfigDirectory { get; }
    bool IsPortableMode { get; }

    /// <summary>Creates the application data folders. Called explicitly during startup.</summary>
    void EnsureDirectories();
}
