using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;
using AlphaLedger.Core.Security;
using AlphaLedger.Core.Services;

namespace AlphaLedger.Tests;

public class CompanyDomainTests
{
    [Fact]
    public void CompanyCodeGenerator_Generates_Sequential_Codes()
    {
        var existing = new[] { "C000001", "C000002", "C000010" };
        var next = CompanyCodeGenerator.NextCode(existing);
        Assert.Equal("C000011", next);

        var empty = Array.Empty<string>();
        var first = CompanyCodeGenerator.NextCode(empty);
        Assert.Equal("C000001", first);
    }

    [Fact]
    public void DuplicateDetector_Detects_Matching_TaxNumber_Or_Normalized_Name()
    {
        var existing = new List<Company>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "C000001",
                DisplayName = "الأهرام للمقاولات",
                LegalNameAr = "شركة الأهرام للمقاولات العامة",
                TaxRegistrationNumber = "123-456-789"
            }
        };

        // Candidate with normalized identical tax registration number
        var candidateWithSameTax = new CompanyEditModel
        {
            DisplayName = "المقاولون الجدد",
            LegalNameAr = "شركة المقاولون",
            TaxRegistrationNumber = "123456789"
        };
        var matches1 = DuplicateCompanyDetector.Find(existing, candidateWithSameTax);
        Assert.NotEmpty(matches1);
        Assert.Contains(matches1, m => m.Reason.Contains("التسجيل الضريبي"));

        // Candidate with normalized identical Arabic name (without ال, أ -> ا, ة -> ه)
        var candidateWithSameName = new CompanyEditModel
        {
            DisplayName = "اهرام للمقاولات", // Normalized will match "الاهرام للمقاولات"
            LegalNameAr = "اي اسم اخر"
        };
        var matches2 = DuplicateCompanyDetector.Find(existing, candidateWithSameName);
        Assert.NotEmpty(matches2);
        Assert.Contains(matches2, m => m.Reason.Contains("المختصر"));
    }

    [Fact]
    public void WindowsPathSafety_Sanitizes_Forbidden_And_Reserved_Names()
    {
        var safe1 = WindowsPathSafety.SanitizeFolderName("شركة / النصر: للتجارة * ؟ \" < > |");
        Assert.DoesNotContain("/", safe1);
        Assert.DoesNotContain(":", safe1);
        Assert.DoesNotContain("*", safe1);
        Assert.DoesNotContain("?", safe1);

        var safeReserved = WindowsPathSafety.SanitizeFolderName("CON");
        Assert.Equal("_CON", safeReserved);
    }
}
