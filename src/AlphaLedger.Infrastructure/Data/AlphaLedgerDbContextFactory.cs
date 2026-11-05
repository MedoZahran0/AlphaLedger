using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlphaLedger.Infrastructure.Data;

public sealed class AlphaLedgerDbContextFactory : IDesignTimeDbContextFactory<AlphaLedgerDbContext>
{
    public AlphaLedgerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AlphaLedgerDbContext>()
            .UseSqlite("Data Source=design-time.db;Foreign Keys=True")
            .Options;
        return new AlphaLedgerDbContext(options);
    }
}
