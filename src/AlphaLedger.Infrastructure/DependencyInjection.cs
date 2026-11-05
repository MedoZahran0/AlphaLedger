using AlphaLedger.Core.Abstractions;
using AlphaLedger.Infrastructure.Data;
using AlphaLedger.Infrastructure.Export;
using AlphaLedger.Infrastructure.Services;
using AlphaLedger.Infrastructure.Security;
using AlphaLedger.Infrastructure.Storage;
using AlphaLedger.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAlphaLedgerInfrastructure(this IServiceCollection services, IAppPaths? paths = null)
    {
        var appPaths = paths ?? new LocalAppPaths();
        services.AddSingleton<IAppPaths>(appPaths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<ICompanyFolderService, CompanyFolderService>();
        services.AddSingleton<ICompanyExcelExporter, CompanyExcelExporter>();
        services.AddSingleton<IWindowsDataProtector, WindowsDpapiDataProtector>();

        services.AddDbContext<AlphaLedgerDbContext>(options =>
        {
            options.UseSqlite(SqliteConnectionConfigurator.BuildConnectionString(appPaths.DatabasePath));
        });

        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<IOperationService, OperationService>();
        services.AddScoped<ITaxReturnService, TaxReturnService>();
        services.AddScoped<IFeeService, FeeService>();
        services.AddScoped<ICollectionService, CollectionService>();
        services.AddScoped<ICompanyPortalAccountService, CompanyPortalAccountService>();
    services.AddScoped<ICompanyArchiveService, CompanyArchiveService>();
    services.AddScoped<IArchiveAttachmentService, ArchiveAttachmentService>();
        return services;
    }
}
