using AlphaLedger.Core.Entities;
using AlphaLedger.Core.Models;
using AlphaLedger.Core.Security;

namespace AlphaLedger.Core.Services;

public static class DuplicateCompanyDetector
{
    public static IReadOnlyList<DuplicateMatch> Find(IEnumerable<Company> existing, CompanyEditModel candidate)
    {
        var matches = new List<DuplicateMatch>();
        var candidateName = WindowsPathSafety.NormalizeComparableName(candidate.DisplayName);
        var candidateLegal = WindowsPathSafety.NormalizeComparableName(candidate.LegalNameAr);
        var candidateTax = WindowsPathSafety.NormalizeTaxNumber(candidate.TaxRegistrationNumber);

        foreach (var company in existing)
        {
            if (candidate.Id.HasValue && candidate.Id.Value == company.Id)
            {
                continue;
            }

            var reasons = new List<string>();
            var existingName = WindowsPathSafety.NormalizeComparableName(company.DisplayName);
            var existingLegal = WindowsPathSafety.NormalizeComparableName(company.LegalNameAr);
            var existingTax = WindowsPathSafety.NormalizeTaxNumber(company.TaxRegistrationNumber);

            if (!string.IsNullOrEmpty(candidateTax) && candidateTax == existingTax)
            {
                reasons.Add("رقم التسجيل الضريبي مطابق");
            }

            if (!string.IsNullOrEmpty(candidateName) && candidateName == existingName)
            {
                reasons.Add("الاسم المختصر مطابق");
            }

            if (!string.IsNullOrEmpty(candidateLegal) && candidateLegal == existingLegal)
            {
                reasons.Add("الاسم القانوني مطابق");
            }

            if (reasons.Count > 0)
            {
                matches.Add(new DuplicateMatch
                {
                    CompanyId = company.Id,
                    Code = company.Code,
                    DisplayName = company.DisplayName,
                    Reason = string.Join("، ", reasons)
                });
            }
        }

        return matches;
    }
}
