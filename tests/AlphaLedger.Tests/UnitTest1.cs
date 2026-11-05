using AlphaLedger.Core.Entities;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.Tests;

public class DatabaseInitializationTests
{
    [Fact]
    public async Task Database_Initializes_And_Creates_All_Tables_Correctly()
    {
        var services = new ServiceCollection();
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"al-test-{Guid.NewGuid():N}.db");

        try
        {
            services.AddDbContext<AlphaLedgerDbContext>(options =>
            {
                options.UseSqlite($"Data Source={tempDbPath};Foreign Keys=True");
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlphaLedgerDbContext>();

            // First launch: create database
            var created = await db.Database.EnsureCreatedAsync();
            Assert.True(created);

            // Apply pragmas
            await SqliteConnectionConfigurator.ApplyPragmasAsync(db);

            // Verify tables can be queried
            Assert.False(await db.Companies.AnyAsync());
            Assert.False(await db.CompanyFiscalYears.AnyAsync());
            Assert.False(await db.Employees.AnyAsync());
            Assert.False(await db.Operations.AnyAsync());
            Assert.False(await db.Fees.AnyAsync());
            Assert.False(await db.Collections.AnyAsync());
            Assert.False(await db.TaxReturns.AnyAsync());
            Assert.False(await db.AppSettings.AnyAsync());
            Assert.False(await db.AuditLogs.AnyAsync());

            // Subsequent launch: EnsureCreated returns false, doesn't throw or delete
            var secondCreated = await db.Database.EnsureCreatedAsync();
            Assert.False(secondCreated);
            await SqliteConnectionConfigurator.ApplyPragmasAsync(db);
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
        }
    }
}
