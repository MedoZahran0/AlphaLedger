namespace AlphaLedger.Core.Models;

public class FolderCheckResult
{
    public bool Exists { get; init; }
    public bool IsAccessible { get; init; }
    public string Path { get; init; } = string.Empty;
    public string? Warning { get; init; }
}
