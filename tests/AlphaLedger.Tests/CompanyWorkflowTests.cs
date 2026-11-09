using AlphaLedger.Core.Abstractions;
using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;
using AlphaLedger.Infrastructure;
using AlphaLedger.Infrastructure.Data;
using AlphaLedger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlphaLedger.Tests;

public class TestAppPaths : IAppPaths
{
    public TestAppPaths(string root)
    {
        ApplicationRoot = root;
        AppDirectory = Path.Combine(root, "App");
        DataDirectory = Path.Combine(root, "Data");
        DocumentsDirectory = Path.Combine(root, "Documents");
        BackupsDirectory = Path.Combine(root, "Backups");
        LogsDirectory = Path.Combine(root, "Data", "logs");
        ConfigDirectory = Path.Combine(root, "Data", "config");
        DatabasePath = Path.Combine(root, "Data", "alpha-ledger.db");
        IsPortableMode = true;
        Directory.CreateDirectory(DataDirectory);
    }

    public string ApplicationRoot { get; }
    public string AppDirectory { get; }
    public string DataDirectory { get; }
    public string DocumentsDirectory { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string DatabasePath { get; }
    public string ConfigDirectory { get; }
    public bool IsPortableMode { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DocumentsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
    }
}

public class CompanyWorkflowTests
{
    [Fact]
    public async Task Archive_Folders_Are_Created_Only_When_Explicitly_Requested()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"al-archive-{Guid.NewGuid():N}");
        var docsRoot = Path.Combine(tempDir, "Archive");
        Directory.CreateDirectory(docsRoot);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAlphaLedgerInfrastructure(new TestAppPaths(tempDir));
            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlphaLedgerDbContext>();
            await db.Database.EnsureCreatedAsync();
            await SqliteConnectionConfigurator.ApplyPragmasAsync(db);

            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetDocumentRootAsync(docsRoot);
            var companyService = scope.ServiceProvider.GetRequiredService<ICompanyService>();
            var folderService = scope.ServiceProvider.GetRequiredService<ICompanyFolderService>();
            var archiveService = scope.ServiceProvider.GetRequiredService<ICompanyArchiveService>();

            var currentYear = DateTime.Today.Year;
            var archiveStartYear = currentYear - 1;
            var company = await companyService.CreateCompanyAsync(new CompanyEditModel
            {
                LegalNameAr = "شركة اختبار الأرشيف",
                DisplayName = "اختبار الأرشيف",
                ArchiveStartYear = archiveStartYear,
                CreateCurrentFiscalYear = true,
                FiscalYearStart = new DateOnly(currentYear, 1, 1),
                FiscalYearEnd = new DateOnly(currentYear, 12, 31)
            });

            var expectedArchivePath = Path.Combine(
                docsRoot,
                archiveStartYear.ToString(),
                folderService.BuildCompanyFolderName(company.DisplayName, company.Code));
            Assert.Equal(archiveStartYear, company.ArchiveStartYear);
            Assert.Equal(expectedArchivePath, company.DocumentStoragePath);
            Assert.False(Directory.Exists(expectedArchivePath));
            var listedCompany = Assert.Single(await companyService.GetCompaniesAsync(new CompanyFilter()));
            Assert.Equal(archiveStartYear, listedCompany.ArchiveStartYear);
            Assert.Equal(expectedArchivePath, listedCompany.DocumentStoragePath);
            Assert.True(listedCompany.FolderPathAssigned);
            Assert.True(listedCompany.FolderMissing);
            Assert.Null(await db.CompanyFiscalYears
                .Where(year => year.CompanyId == company.Id)
                .Select(year => year.FolderPath)
                .SingleAsync());

            var secondCompany = await companyService.CreateCompanyAsync(new CompanyEditModel
            {
                LegalNameAr = "شركة ثانية في نفس سنة الأرشيف",
                ArchiveStartYear = archiveStartYear,
                CreateCurrentFiscalYear = false
            });
            Assert.NotNull(secondCompany.DocumentStoragePath);
            Assert.Equal(Path.GetDirectoryName(expectedArchivePath), Path.GetDirectoryName(secondCompany.DocumentStoragePath));
            Assert.False(Directory.Exists(expectedArchivePath));

            await archiveService.EnsureCurrentYearStructureAsync();
            Assert.True(Directory.Exists(Path.Combine(docsRoot, currentYear.ToString())));
            Assert.False(Directory.Exists(expectedArchivePath));

            await archiveService.EnsureCompanyFoldersAsync(company.Id, company.Code);
            Assert.Equal(expectedArchivePath, company.DocumentStoragePath);
            Assert.True(Directory.Exists(expectedArchivePath));
            Assert.Contains("01 - مستندات الشركة العامة", Directory.GetDirectories(expectedArchivePath).Select(Path.GetFileName));
            Assert.Contains("99 - المحذوفات", Directory.GetDirectories(expectedArchivePath).Select(Path.GetFileName));

            var nextYear = currentYear + 1;
            var fiscalYear = await companyService.AddFiscalYearAsync(company.Id, new FiscalYearEditModel
            {
                YearLabel = nextYear.ToString(),
                StartDate = new DateOnly(nextYear, 1, 1),
                EndDate = new DateOnly(nextYear, 12, 31),
                Status = AlphaLedger.Core.Enums.FiscalYearStatus.Planned
            });

            var expectedNextPath = Path.Combine(
                docsRoot,
                nextYear.ToString(),
                folderService.BuildCompanyFolderName(company.DisplayName, company.Code));
            Assert.Equal(expectedNextPath, fiscalYear.FolderPath);
            Assert.True(Directory.Exists(expectedNextPath));
            Assert.Contains("01 - مستندات الشركة العامة", Directory.GetDirectories(expectedNextPath).Select(Path.GetFileName));

            await archiveService.EnsureCurrentYearStructureAsync();
            Assert.True(Directory.Exists(Path.Combine(docsRoot, currentYear.ToString())));
            Assert.True(Directory.Exists(expectedArchivePath));
            Assert.Equal(expectedArchivePath, company.DocumentStoragePath);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Archive_Year_Copy_Copies_Only_Folder_Structure_And_Merges_Existing_Folders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"al-archive-copy-{Guid.NewGuid():N}");
        var docsRoot = Path.Combine(tempDir, "Archive");
        var sourceCompany = Path.Combine(docsRoot, "2026", "Company_A");
        var targetCompany = Path.Combine(docsRoot, "2027", "Company_A");
        Directory.CreateDirectory(Path.Combine(sourceCompany, "01 - General", "Empty"));
        Directory.CreateDirectory(Path.Combine(sourceCompany, "02 - Tax"));
        Directory.CreateDirectory(Path.Combine(targetCompany, "01 - General"));
        Directory.CreateDirectory(Path.Combine(targetCompany, "03 - Existing"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(sourceCompany, "tax-return.pdf"), "source document");
        File.WriteAllText(Path.Combine(sourceCompany, "02 - Tax", "declaration.pdf"), "nested source document");
        File.WriteAllText(Path.Combine(targetCompany, "01 - General", "keep.txt"), "existing nested file");
        File.WriteAllText(Path.Combine(targetCompany, "keep.txt"), "existing destination file");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAlphaLedgerInfrastructure(new TestAppPaths(tempDir));
            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlphaLedgerDbContext>();
            await db.Database.EnsureCreatedAsync();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetDocumentRootAsync(docsRoot);
            var archiveService = scope.ServiceProvider.GetRequiredService<ICompanyArchiveService>();

            var copied = await archiveService.CopyCompanyFolderStructureToYearAsync("2026", "2027");

            Assert.Equal(1, copied);
            Assert.True(Directory.Exists(Path.Combine(targetCompany, "01 - General", "Empty")));
            Assert.True(Directory.Exists(Path.Combine(targetCompany, "02 - Tax")));
            Assert.True(Directory.Exists(Path.Combine(targetCompany, "03 - Existing")));
            Assert.False(File.Exists(Path.Combine(targetCompany, "tax-return.pdf")));
            Assert.False(File.Exists(Path.Combine(targetCompany, "02 - Tax", "declaration.pdf")));
            Assert.Equal("existing nested file", await File.ReadAllTextAsync(Path.Combine(targetCompany, "01 - General", "keep.txt")));
            Assert.Equal("existing destination file", await File.ReadAllTextAsync(Path.Combine(targetCompany, "keep.txt")));
            Assert.True(File.Exists(Path.Combine(sourceCompany, "tax-return.pdf")));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Complete_Company_Lifecycle_And_Excel_Export_Work_Correctly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"al-work-{Guid.NewGuid():N}");
        var docsRoot = Path.Combine(tempDir, "CompanyDocs");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(docsRoot);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAlphaLedgerInfrastructure(new TestAppPaths(tempDir));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlphaLedgerDbContext>();
            await db.Database.EnsureCreatedAsync();
            await SqliteConnectionConfigurator.ApplyPragmasAsync(db);
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetDocumentRootAsync(docsRoot);
            var companyService = scope.ServiceProvider.GetRequiredService<ICompanyService>();
            var portalAccounts = scope.ServiceProvider.GetRequiredService<ICompanyPortalAccountService>();
            var dataProtector = scope.ServiceProvider.GetRequiredService<IWindowsDataProtector>();

            var createModel = new CompanyEditModel
            {
                LegalNameAr = "شركة الأفق للاستشارات الهندسية",
                DisplayName = "الأفق للاستشارات",
                TaxRegistrationNumber = "456-789-012",
                CreateCurrentFiscalYear = true,
                FiscalYearStart = new DateOnly(2026, 1, 1),
                FiscalYearEnd = new DateOnly(2026, 12, 31)
            };
            var company = await companyService.CreateCompanyAsync(createModel);

            if (OperatingSystem.IsWindows())
            {
                const string portalPassword = "Test-Portal-Secret-42";
                var portalAccount = await portalAccounts.AddAsync(new CompanyPortalAccount
                {
                    CompanyId = company.Id,
                    PortalName = "Test Tax Portal",
                    Url = "https://example.invalid/portal",
                    Username = "test-user"
                }, portalPassword);

                var persistedAccount = Assert.Single(await portalAccounts.GetForCompanyAsync(company.Id));
                Assert.NotNull(persistedAccount.ProtectedPassword);
                Assert.Equal(portalPassword, System.Text.Encoding.UTF8.GetString(dataProtector.Unprotect(persistedAccount.ProtectedPassword)));

                portalAccount.PortalName = "Updated Test Portal";
                var updatedPortal = await portalAccounts.UpdateAsync(portalAccount, newPassword: null);
                Assert.Equal(portalAccount.ProtectedPassword, updatedPortal.ProtectedPassword);

                await portalAccounts.DeleteAsync(portalAccount.Id);
                Assert.Empty(await portalAccounts.GetForCompanyAsync(company.Id));
            }
            var feeService = scope.ServiceProvider.GetRequiredService<IFeeService>();
            var collectionService = scope.ServiceProvider.GetRequiredService<ICollectionService>();
            var operationService = scope.ServiceProvider.GetRequiredService<IOperationService>();
            var taxReturnService = scope.ServiceProvider.GetRequiredService<ITaxReturnService>();
            var fiscalYear = Assert.Single(await companyService.GetFiscalYearsAsync(company.Id));
            var operation = await operationService.AddOperationAsync(new OfficeOperation
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FiscalYearId = fiscalYear.Id,
                ServiceCategory = "VAT return preparation",
                Description = "Prepare quarterly VAT return",
                StartDate = new DateOnly(2026, 1, 1),
                DueDate = new DateOnly(2026, 4, 30),
                Status = AlphaLedger.Core.Enums.OperationStatus.InProgress,
                Priority = AlphaLedger.Core.Enums.OperationPriority.High,
                CreatedAtUtc = DateTime.UtcNow
            });
            Assert.Equal(AlphaLedger.Core.Enums.OperationPriority.High, operation.Priority);
            var companyOperations = await operationService.GetOperationsAsync(company.Id);
            Assert.Contains(companyOperations, x => x.Id == operation.Id);

            var taxReturn = await taxReturnService.AddTaxReturnAsync(new TaxReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FiscalYearId = fiscalYear.Id,
                TaxType = " vat ",
                TaxPeriod = " 2026-Q1 ",
                FilingDueDate = new DateOnly(2026, 4, 30),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(() => taxReturnService.AddTaxReturnAsync(new TaxReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FiscalYearId = fiscalYear.Id,
                TaxType = " vAt ",
                TaxPeriod = " 2026-Q1 ",
                FilingDueDate = new DateOnly(2026, 4, 30)
            }));
            var nextFiscalYear = await companyService.AddFiscalYearAsync(company.Id, new FiscalYearEditModel
            {
                YearLabel = "2027",
                StartDate = new DateOnly(2027, 1, 1),
                EndDate = new DateOnly(2027, 12, 31),
                Status = AlphaLedger.Core.Enums.FiscalYearStatus.Planned
            });
            var nextYearReturn = await taxReturnService.AddTaxReturnAsync(new TaxReturn
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FiscalYearId = nextFiscalYear.Id,
                TaxType = " vat ",
                TaxPeriod = "2026-Q1",
                FilingDueDate = new DateOnly(2027, 4, 30)
            });
            Assert.Equal(nextFiscalYear.Id, nextYearReturn.FiscalYearId);
            await taxReturnService.MarkSubmittedAsync(taxReturn.Id, "TAX-RECEIPT-1");
            Assert.Equal(AlphaLedger.Core.Enums.TaxReturnFilingStatus.Submitted,
                (await taxReturnService.GetTaxReturnAsync(taxReturn.Id))?.FilingStatus);

            var fee = await feeService.AddFeeAsync(new Fee
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FiscalYearId = fiscalYear.Id,
                Description = "Quarterly accounting service",
                FeeDate = new DateOnly(2026, 1, 1),
                Amount = 1000m,
                Currency = "EGP",
                OperationId = operation.Id,
                Status = AlphaLedger.Core.Enums.FeeStatus.Agreed
            });

            Assert.Equal(1000m, await collectionService.GetOutstandingBalanceAsync(fee.Id));
            var collection = await collectionService.AddCollectionAsync(new Collection
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FeeId = fee.Id,
                CollectionDate = new DateOnly(2026, 2, 1),
                AmountReceived = 600m,
                PaymentMethod = "Bank transfer",
                ReceiptNumber = "TST-REC-001"
            });
            Assert.Equal(400m, await collectionService.GetOutstandingBalanceAsync(fee.Id));
            Assert.Equal(AlphaLedger.Core.Enums.FeeStatus.PartiallyCollected,
                (await feeService.GetFeeAsync(fee.Id))?.Status);
            await db.Fees
                .Where(item => item.Id == fee.Id)
                .ExecuteUpdateAsync(updates => updates.SetProperty(item => item.Status, AlphaLedger.Core.Enums.FeeStatus.Agreed));
            Assert.Equal(AlphaLedger.Core.Enums.FeeStatus.PartiallyCollected,
                Assert.Single(await feeService.GetFeesAsync(company.Id, AlphaLedger.Core.Enums.FeeStatus.PartiallyCollected)).Status);

            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(() => collectionService.AddCollectionAsync(new Collection
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FeeId = fee.Id,
                CollectionDate = new DateOnly(2026, 2, 2),
                AmountReceived = 401m,
                PaymentMethod = "Cash",
                ReceiptNumber = "TST-REC-002"
            }));
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(() => collectionService.AddCollectionAsync(new Collection
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FeeId = fee.Id,
                CollectionDate = new DateOnly(2026, 2, 2),
                AmountReceived = 1m,
                PaymentMethod = "Cash",
                ReceiptNumber = collection.ReceiptNumber
            }));

            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(
                () => collectionService.DeleteCollectionAsync(collection.Id));
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(
                () => feeService.DeleteFeeAsync(fee.Id));

            var feeWithReducedAmount = await feeService.GetFeeAsync(fee.Id);
            Assert.NotNull(feeWithReducedAmount);
            feeWithReducedAmount.Amount = 500m;
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(
                () => feeService.UpdateFeeAsync(feeWithReducedAmount));

            await collectionService.ReverseCollectionAsync(collection.Id, "Incorrect amount entered");
            Assert.Equal(1000m, await collectionService.GetOutstandingBalanceAsync(fee.Id));
            Assert.Equal("Incorrect amount entered", (await collectionService.GetCollectionAsync(collection.Id))?.ReversalReason);
            Assert.Equal(AlphaLedger.Core.Enums.FeeStatus.Agreed,
                (await feeService.GetFeeAsync(fee.Id))?.Status);
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(
                () => operationService.DeleteOperationAsync(operation.Id));

            var finalCollection = await collectionService.AddCollectionAsync(new Collection
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                FeeId = fee.Id,
                CollectionDate = new DateOnly(2026, 2, 3),
                AmountReceived = 1000m,
                PaymentMethod = "Cash",
                ReceiptNumber = "TST-REC-003"
            });
            Assert.Equal(AlphaLedger.Core.Enums.FeeStatus.Collected,
                (await feeService.GetFeeAsync(fee.Id))?.Status);
            Assert.False(await collectionService.CanCollectAsync(fee.Id, 1m));
            Assert.False(finalCollection.IsReversed);
            var collectedFee = await feeService.GetFeeAsync(fee.Id);
            Assert.NotNull(collectedFee);
            collectedFee.Status = AlphaLedger.Core.Enums.FeeStatus.Cancelled;
            await Assert.ThrowsAsync<AlphaLedger.Core.Exceptions.BusinessException>(() =>
                feeService.UpdateFeeAsync(collectedFee));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}