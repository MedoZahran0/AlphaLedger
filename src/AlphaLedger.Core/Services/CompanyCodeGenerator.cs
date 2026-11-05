namespace AlphaLedger.Core.Services;

public static class CompanyCodeGenerator
{
    public static string NextCode(IEnumerable<string> existingCodes)
    {
        var max = 0;
        foreach (var code in existingCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var digits = new string(code.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return $"C{(max + 1):000000}";
    }
}
