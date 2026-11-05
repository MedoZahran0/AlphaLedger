using System.Security.Cryptography;
using System.Text;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Exceptions;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Services;

public sealed class CompanyPortalAccountService : ICompanyPortalAccountService
{
    private readonly AlphaLedgerDbContext _db;
    private readonly IWindowsDataProtector _protector;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public CompanyPortalAccountService(
        AlphaLedgerDbContext db,
        IWindowsDataProtector protector,
        IAuditService audit,
        IClock clock)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CompanyPortalAccount>> GetForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
        => await _db.CompanyPortalAccounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .OrderBy(account => account.PortalName)
            .ToListAsync(cancellationToken);

    public async Task<CompanyPortalAccount> AddAsync(CompanyPortalAccount account, string? password, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(account, cancellationToken);
        var now = _clock.UtcNow;
        account.Id = account.Id == Guid.Empty ? Guid.NewGuid() : account.Id;
        account.CreatedAtUtc = now;
        account.UpdatedAtUtc = now;
        account.ProtectedPassword = ProtectPassword(password);

        _db.CompanyPortalAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Create", nameof(CompanyPortalAccount), account.Id.ToString(), "Added a company portal account.", cancellationToken: cancellationToken);
        return account;
    }

    public async Task<CompanyPortalAccount> UpdateAsync(CompanyPortalAccount account, string? newPassword, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(account, cancellationToken);
        var existing = await _db.CompanyPortalAccounts.FirstOrDefaultAsync(item => item.Id == account.Id, cancellationToken)
            ?? throw new BusinessException("Portal account not found.");

        existing.PortalName = account.PortalName.Trim();
        existing.Url = account.Url.Trim();
        existing.Username = account.Username.Trim();
        if (!string.IsNullOrWhiteSpace(newPassword))
            existing.ProtectedPassword = ProtectPassword(newPassword);
        existing.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Update", nameof(CompanyPortalAccount), existing.Id.ToString(), "Updated a company portal account.", cancellationToken: cancellationToken);
        return existing;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _db.CompanyPortalAccounts.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new BusinessException("Portal account not found.");
        _db.CompanyPortalAccounts.Remove(account);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("Delete", nameof(CompanyPortalAccount), id.ToString(), "Deleted a company portal account.", cancellationToken: cancellationToken);
    }

    private async Task ValidateAsync(CompanyPortalAccount account, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.PortalName))
            throw new BusinessException("Portal name is required.");
        if (string.IsNullOrWhiteSpace(account.Username))
            throw new BusinessException("Portal username is required.");
        if (!Uri.TryCreate(account.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new BusinessException("Portal URL must be a valid HTTPS address.");
        if (!await _db.Companies.AnyAsync(company => company.Id == account.CompanyId, cancellationToken))
            throw new BusinessException("Company not found.");
    }

    private byte[]? ProtectPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return null;

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return _protector.Protect(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}