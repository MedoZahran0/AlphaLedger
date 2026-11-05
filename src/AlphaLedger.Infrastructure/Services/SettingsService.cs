using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IClock _clock;

    public SettingsService(AlphaLedgerDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (setting is null)
        {
            setting = new AppSetting { Key = key, Value = value, UpdatedAtUtc = _clock.UtcNow };
            _db.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<string?> GetDocumentRootAsync(CancellationToken cancellationToken = default)
        => GetAsync(SettingKeys.DocumentRootPath, cancellationToken);

    public Task SetDocumentRootAsync(string path, CancellationToken cancellationToken = default)
        => SetAsync(SettingKeys.DocumentRootPath, path, cancellationToken);
}
