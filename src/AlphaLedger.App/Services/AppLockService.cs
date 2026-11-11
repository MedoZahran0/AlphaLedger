using System.Security.Cryptography;
using System.Globalization;
using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.App.Services;

public sealed class AppLockService
{
    private const int Iterations = 310_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private readonly IServiceScopeFactory _scopeFactory;

    public AppLockService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> IsPasswordConfiguredAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        return !string.IsNullOrWhiteSpace(
            await settings.GetAsync(SettingKeys.AppLockPasswordHash, cancellationToken));
    }

    public async Task<bool> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var storedValue = await settings.GetAsync(SettingKeys.AppLockPasswordHash, cancellationToken);
        if (!TryParseHash(storedValue, out var iterations, out var salt, out var expectedHash))
            return false;

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualHash);
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    public async Task ConfigurePasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);

        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        if (!string.IsNullOrWhiteSpace(
                await settings.GetAsync(SettingKeys.AppLockPasswordHash, cancellationToken)))
            throw new InvalidOperationException("An application password is already configured.");

        await settings.SetAsync(SettingKeys.AppLockPasswordHash, CreateHash(password), cancellationToken);
    }

    public async Task<bool> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        if (!await VerifyPasswordAsync(currentPassword, cancellationToken))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(SettingKeys.AppLockPasswordHash, CreateHash(newPassword), cancellationToken);
        return true;
    }

    private static string CreateHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        try
        {
            return $"v1${Iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    private static bool TryParseHash(
        string? value,
        out int iterations,
        out byte[] salt,
        out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = value?.Split('$');
        if (parts is not { Length: 4 } ||
            parts[0] != "v1" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations) ||
            iterations is < 100_000 or > 2_000_000)
            return false;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return salt.Length == SaltSize && hash.Length == HashSize;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 4)
            throw new ArgumentException("The application password must contain at least 4 characters.", nameof(password));
    }
}
