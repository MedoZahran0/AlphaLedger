using System.IO;
using System.Data;
using System.Diagnostics;
using System.Windows;
using AlphaLedger.App.Localization;
using AlphaLedger.App.Services;
using AlphaLedger.App.ViewModels;
using AlphaLedger.App.Views;
using AlphaLedger.Core;
using AlphaLedger.Core.Abstractions;
using AlphaLedger.Infrastructure;
using AlphaLedger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AlphaLedger.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent unexpected browser/WebView2 errors from terminating the application
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger", "app-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] DispatcherUnhandledException: {args.Exception}\n");
            }
            catch
            {
            }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger", "app-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] UnhandledException: {args.ExceptionObject}\n");
            }
            catch
            {
            }
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger", "app-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] UnobservedTaskException: {args.Exception}\n");
            }
            catch
            {
            }
            args.SetObserved();
        };

        // Prevent WPF from shutting down during asynchronous initialization before MainWindow is shown
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var loc = LocalizationService.Instance;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Infrastructure services (DbContext, FileSystem, Folders, Settings, etc.)
                    services.AddAlphaLedgerInfrastructure();

                    // UI Services
                    services.AddSingleton<WindowsDialogService>();
                    services.AddSingleton<WindowsFolderPicker>();
                    services.AddSingleton<GitHubUpdateService>();
                    services.AddTransient<AppDataRelocationService>();

                    // ViewModels
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<DashboardViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<FiscalYearsViewModel>();
                    services.AddTransient<OfficeOperationsViewModel>();
                    services.AddTransient<TaxReturnsViewModel>();
                    services.AddTransient<StaffViewModel>();
                    services.AddTransient<ReportsViewModel>();

                    // Windows
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            // Path objects are pure; folder creation happens explicitly here.
            _host.Services.GetRequiredService<IAppPaths>().EnsureDirectories();

            if (e.Args.Contains("--validate-localization", StringComparer.OrdinalIgnoreCase))
            {
                var issues = loc.GetCompletenessIssues().ToList();
                var originalCulture = loc.CurrentCulture.Name;
                foreach (var culture in new[] { "ar-EG", "en-US", "ar-EG", "en-US" })
                {
                    loc.SetLanguage(culture);
                    var isArabic = culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
                    var hasArabicText = loc.GetString("AppTitle").Any(character => character is >= '\u0600' and <= '\u06FF');

                    if (loc.GetFlowDirection() != (isArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))
                        issues.Add($"Flow direction is incorrect for '{culture}'.");
                    if (hasArabicText != isArabic)
                        issues.Add($"AppTitle translation is incorrect for '{culture}'.");
                }
                loc.SetLanguage(originalCulture);

                if (issues.Count == 0)
                {
                    Console.WriteLine("Localization catalogs and repeated culture switches are valid.");
                }
                else
                {
                    foreach (var issue in issues)
                    {
                        Console.WriteLine(issue);
                    }
                }

                Shutdown(issues.Count == 0 ? 0 : 1);
                return;
            }

            // Initialize SQLite database and connection pragmas
            await InitializeDatabaseAsync(_host.Services);

            // Make sure the current year folder and an empty folder for every active company exist.
            using (var archiveScope = _host.Services.CreateScope())
            {
                var archive = archiveScope.ServiceProvider.GetRequiredService<ICompanyArchiveService>();
                await archive.EnsureCurrentYearStructureAsync();
            }

            using (var scope = _host.Services.CreateScope())
            {
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var savedLanguage = await settings.GetAsync(SettingKeys.SelectedLanguage);
                if (!string.IsNullOrWhiteSpace(savedLanguage))
                    loc.SetLanguage(savedLanguage);
            }

            if (e.Args.Length > 0 && e.Args[0] == "--capture-screens")
            {
                var targetDir = e.Args.Length > 1 ? e.Args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger", "screenshots");
                await UiScreenCaptureHelper.CaptureScreensAsync(_host.Services, targetDir);
                Shutdown();
                return;
            }

            // Resolve and show the main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            await CheckForUpdatesAsync(mainWindow, loc);
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlphaLedger", "app-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] StartupException: {ex}\n");
            }
            catch
            {
            }

            MessageBox.Show(
                string.Format(loc.CurrentCulture, loc.GetString("StartupErrorMessage"), loc.TranslateMessage(ex.Message)),
                loc.GetString("StartupErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private async Task CheckForUpdatesAsync(MainWindow mainWindow, LocalizationService localization)
    {
        var updater = _host!.Services.GetRequiredService<GitHubUpdateService>();
        GitHubUpdateService.ReleaseUpdate? update;
        try
        {
            update = await updater.CheckForUpdateAsync();
            if (update is null || !mainWindow.IsVisible)
                return;
        }
        catch (Exception ex)
        {
            LogUpdateError(ex);
            return;
        }

        var message = string.Format(localization.CurrentCulture,
            localization.GetString("UpdateAvailableMessage"), update.TagName);
        var answer = MessageBox.Show(mainWindow, message,
            localization.GetString("UpdateAvailableTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var installerPath = await updater.DownloadInstallerAsync(update);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            Shutdown();
        }
        catch (Exception ex)
        {
            LogUpdateError(ex);
            MessageBox.Show(mainWindow,
                localization.GetString("UpdateCheckFailedMessage"),
                localization.GetString("UpdateAvailableTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void LogUpdateError(Exception exception)
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlphaLedger", "app-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] UpdateCheckException: {exception}\n");
        }
        catch
        {
        }
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlphaLedgerDbContext>();

        // Ensure the SQLite database file and tables are created
        await db.Database.EnsureCreatedAsync();

        // Add the portal-account table safely for databases created by earlier builds.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "CompanyPortalAccounts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_CompanyPortalAccounts" PRIMARY KEY,
                "CompanyId" TEXT NOT NULL,
                "PortalName" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "Username" TEXT NOT NULL,
                "ProtectedPassword" BLOB NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_CompanyPortalAccounts_Companies_CompanyId"
                    FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_CompanyPortalAccounts_CompanyId"
                ON "CompanyPortalAccounts" ("CompanyId");
            """);
        await EnsureSqliteColumnAsync(db, "Fees", "FiscalYearId", "TEXT NULL");
        await EnsureSqliteColumnAsync(db, "Collections", "ReversalReason", "TEXT NULL");
        await EnsureSqliteColumnAsync(db, "Operations", "Priority", "INTEGER NOT NULL DEFAULT 1");
        await EnsureSqliteColumnAsync(db, "Companies", "ArchiveStartYear", "INTEGER NULL");
        await EnsureArchiveAttachmentsTableAsync(db);
        await EnsureSqliteColumnAsync(db, "ArchiveAttachments", "Source", "TEXT NULL");
        // Apply SQLite operational PRAGMAs (WAL mode, foreign keys, busy timeout)
        await SqliteConnectionConfigurator.ApplyPragmasAsync(db);

        // Set default documents archive folder if not yet configured
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var currentRoot = await settings.GetDocumentRootAsync();
        if (string.IsNullOrWhiteSpace(currentRoot))
        {
            var paths = scope.ServiceProvider.GetRequiredService<IAppPaths>();
            var defaultRoot = paths.DocumentsDirectory;
            Directory.CreateDirectory(defaultRoot);
            await settings.SetDocumentRootAsync(defaultRoot);
            currentRoot = defaultRoot;
        }

        var companiesWithoutArchiveYear = await db.Companies
            .Where(company => company.ArchiveStartYear == null)
            .ToListAsync();
        foreach (var company in companiesWithoutArchiveYear)
        {
            var yearFolder = Path.GetFileName(Path.GetDirectoryName(company.DocumentStoragePath ?? string.Empty));
            company.ArchiveStartYear = int.TryParse(
                yearFolder,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var archiveYear) && archiveYear is >= 1900 and <= 9999
                ? archiveYear
                : DateTime.Today.Year;
        }

        if (companiesWithoutArchiveYear.Count > 0)
            await db.SaveChangesAsync();
    }

    private static async Task EnsureArchiveAttachmentsTableAsync(AlphaLedgerDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ""ArchiveAttachments"" (
    ""Id"" TEXT NOT NULL PRIMARY KEY,
    ""CompanyId"" TEXT NOT NULL,
    ""EntityType"" TEXT NOT NULL,
    ""EntityId"" TEXT NOT NULL,
    ""CategoryNumber"" INTEGER NOT NULL,
    ""FilePath"" TEXT NOT NULL,
    ""FileName"" TEXT NOT NULL,
    ""AttachedAtUtc"" TEXT NOT NULL
);";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static async Task EnsureSqliteColumnAsync(DbContext db, string tableName, string columnName, string columnDefinition)
    {
        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
            await connection.OpenAsync();

        try
        {
            var columnExists = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            if (!columnExists)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition};";
                await alter.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (shouldCloseConnection)
                await connection.CloseAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
