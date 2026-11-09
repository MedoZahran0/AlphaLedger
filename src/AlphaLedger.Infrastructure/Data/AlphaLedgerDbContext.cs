using AlphaLedger.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlphaLedger.Infrastructure.Data;

public class AlphaLedgerDbContext : DbContext
{
    public AlphaLedgerDbContext(DbContextOptions<AlphaLedgerDbContext> options) : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyFiscalYear> CompanyFiscalYears => Set<CompanyFiscalYear>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<OfficeOperation> Operations => Set<OfficeOperation>();
    public DbSet<Fee> Fees => Set<Fee>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<TaxReturn> TaxReturns => Set<TaxReturn>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CompanyPortalAccount> CompanyPortalAccounts => Set<CompanyPortalAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).IsRequired().HasMaxLength(32);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.LegalNameAr).IsRequired().HasMaxLength(250);
            entity.Property(x => x.DisplayName).IsRequired().HasMaxLength(150);
            entity.Property(x => x.EnglishName).HasMaxLength(250);
            entity.Property(x => x.TaxRegistrationNumber).HasMaxLength(50);
            entity.Property(x => x.TaxFileNumber).HasMaxLength(50);
            entity.Property(x => x.TaxOffice).HasMaxLength(120);
            entity.Property(x => x.BusinessActivity).HasMaxLength(200);
            entity.Property(x => x.LegalForm).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.DocumentStoragePath).HasMaxLength(1000);
            entity.HasIndex(x => x.TaxRegistrationNumber);
            entity.HasIndex(x => x.DisplayName);
            entity.HasOne(x => x.AssignedEmployee)
                .WithMany(x => x.AssignedCompanies)
                .HasForeignKey(x => x.AssignedEmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CompanyFiscalYear>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.YearLabel).IsRequired().HasMaxLength(20);
            entity.HasIndex(x => new { x.CompanyId, x.YearLabel }).IsUnique();
            entity.HasOne(x => x.Company)
                .WithMany(x => x.FiscalYears)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.JobTitle).HasMaxLength(120);
        });

        modelBuilder.Entity<OfficeOperation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgreedFee).HasPrecision(18, 2);
            entity.Property(x => x.Priority).HasDefaultValue(AlphaLedger.Core.Enums.OperationPriority.Medium).HasSentinel((AlphaLedger.Core.Enums.OperationPriority)(-1));
            entity.HasOne(x => x.Company)
                .WithMany(x => x.Operations)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FiscalYear)
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.AssignedEmployee)
                .WithMany()
                .HasForeignKey(x => x.AssignedEmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.CompanyId, x.Status });
        });

        modelBuilder.Entity<Fee>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(8);
            entity.HasIndex(x => x.FiscalYearId);
            entity.HasOne(x => x.Company)
                .WithMany(x => x.Fees)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FiscalYear)
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Operation)
                .WithMany()
                .HasForeignKey(x => x.OperationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AmountReceived).HasPrecision(18, 2);
            entity.Property(x => x.PaymentMethod).HasMaxLength(50);
            entity.Property(x => x.ReversalReason).HasMaxLength(500);
            entity.HasIndex(x => x.ReceiptNumber);
            entity.HasOne(x => x.Company)
                .WithMany(x => x.Collections)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Fee)
                .WithMany(x => x.Collections)
                .HasForeignKey(x => x.FeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaxReturn>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Company)
                .WithMany(x => x.TaxReturns)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.FiscalYear)
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.ResponsibleEmployee)
                .WithMany()
                .HasForeignKey(x => x.ResponsibleEmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RelatedOperation)
                .WithMany()
                .HasForeignKey(x => x.RelatedOperationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RelatedFee)
                .WithMany()
                .HasForeignKey(x => x.RelatedFeeId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.CompanyId, x.FilingDueDate });
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(100);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(80);
            entity.Property(x => x.EntityType).HasMaxLength(80);
            entity.HasIndex(x => x.OccurredAtUtc);
        });

        modelBuilder.Entity<CompanyPortalAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PortalName).IsRequired().HasMaxLength(120);
            entity.Property(x => x.Url).IsRequired().HasMaxLength(2048);
            entity.Property(x => x.Username).IsRequired().HasMaxLength(256);
            entity.Property(x => x.ProtectedPassword).HasColumnType("BLOB");
            entity.HasIndex(x => x.CompanyId);
            entity.HasOne(x => x.Company)
                .WithMany(x => x.PortalAccounts)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
