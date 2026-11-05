namespace AlphaLedger.Core.Abstractions;

public interface ISettingsService
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);
    Task<string?> GetDocumentRootAsync(CancellationToken cancellationToken = default);
    Task SetDocumentRootAsync(string path, CancellationToken cancellationToken = default);
}
