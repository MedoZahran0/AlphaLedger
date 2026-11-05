namespace AlphaLedger.Core.Entities;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
