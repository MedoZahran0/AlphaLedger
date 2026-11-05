using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;

namespace AlphaLedger.App.Localization;

/// <summary>
/// Centralized localization service for Alpha Ledger.
/// Manages language switching between English (default) and Arabic (RTL).
/// Uses DynamicResource-compatible string dictionaries.
/// </summary>
public sealed class LocalizationService
{
    private static readonly LocalizationService _instance = new();
    public static LocalizationService Instance => _instance;

    private readonly ResourceDictionary _englishDict = new();
    private readonly ResourceDictionary _arabicDict = new();
    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en-US");

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            _currentCulture = value;
            CultureInfo.DefaultThreadCurrentCulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            Thread.CurrentThread.CurrentCulture = value;
            Thread.CurrentThread.CurrentUICulture = value;
        }
    }

    public event Action? CultureChanged;

    public static string ValidationDescriptionRequired => Instance.GetString("ValidationDescriptionRequired");
    public static string ValidationAmountRequired => Instance.GetString("ValidationAmountRequired");
    public static string ValidationCurrencyRequired => Instance.GetString("ValidationCurrencyRequired");
    public static string ValidationCurrencyLength => Instance.GetString("ValidationCurrencyLength");
    public static string ValidationPaymentMethodRequired => Instance.GetString("ValidationPaymentMethodRequired");
    public static string ValidationPaymentMethodLength => Instance.GetString("ValidationPaymentMethodLength");

    public string GetString(string key)
    {
        return Application.Current?.TryFindResource(key) as string ?? key;
    }

    public IReadOnlyList<string> GetCompletenessIssues()
    {
        var issues = new List<string>();
        var englishKeys = _englishDict.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        var arabicKeys = _arabicDict.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var key in englishKeys.Except(arabicKeys, StringComparer.Ordinal))
            issues.Add($"Arabic translation is missing for '{key}'.");
        foreach (var key in arabicKeys.Except(englishKeys, StringComparer.Ordinal))
            issues.Add($"English translation is missing for '{key}'.");

        foreach (var key in englishKeys.Intersect(arabicKeys, StringComparer.Ordinal))
        {
            var english = _englishDict[key] as string;
            var arabic = _arabicDict[key] as string;
            if (string.IsNullOrWhiteSpace(english) || string.IsNullOrWhiteSpace(arabic))
            {
                issues.Add($"Translation for '{key}' is empty.");
                continue;
            }

            if (Regex.IsMatch(english, "[\\u0600-\\u06FF]"))
                issues.Add($"English translation for '{key}' contains Arabic characters.");

            if (string.Equals(english.Trim(), arabic.Trim(), StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(english, @"[A-Za-z\u0600-\u06FF]"))
                issues.Add($"Translations for '{key}' are identical.");

            var englishArguments = Regex.Matches(english, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value);
            var arabicArguments = Regex.Matches(arabic, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value);
            if (!englishArguments.SequenceEqual(arabicArguments, StringComparer.Ordinal))
                issues.Add($"Placeholder arguments differ for '{key}'.");
        }

        var sourceDirectory = Path.Combine(Environment.CurrentDirectory, "src", "AlphaLedger.App");
        if (Directory.Exists(sourceDirectory))
        {
            var dynamicResourcePattern = new Regex(@"\{DynamicResource\s+([^,\s}]+)");
            var visibleTextPattern = new Regex(@"\b(?:Text|Content|Header|ToolTip|Title)\s*=\s*""([^""]*)""", RegexOptions.Compiled);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*.xaml", SearchOption.AllDirectories))
            {
                if (Path.GetRelativePath(sourceDirectory, file).StartsWith($"Localization{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    continue;

                var markup = File.ReadAllText(file);
                foreach (Match match in dynamicResourcePattern.Matches(markup))
                {
                    var key = match.Groups[1].Value;
                    if (!englishKeys.Contains(key) || !arabicKeys.Contains(key))
                        issues.Add($"XAML resource reference '{key}' is missing from a language catalog.");
                }

                foreach (Match match in visibleTextPattern.Matches(markup))
                {
                    var text = match.Groups[1].Value.Trim();
                    if (text.Length == 0 || text == "AL" || text.StartsWith('{'))
                        continue;
                    if (Regex.IsMatch(text, @"[A-Za-z\u0600-\u06FF]"))
                        issues.Add($"Hardcoded XAML UI text in '{Path.GetRelativePath(sourceDirectory, file)}': {text}");
                }
            }
        }

        return issues;
    }

    public string TranslateMessage(string message)
    {
        var source = CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? _englishDict
            : _arabicDict;
        var target = CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            ? _arabicDict
            : _englishDict;

        foreach (var key in source.Keys)
        {
            if (source[key] is not string sourceText || target[key] is not string targetText)
                continue;

            if (string.Equals(message, sourceText, StringComparison.Ordinal))
                return targetText;

            var sourceTokens = Regex.Matches(sourceText, @"\{(\d+)(?:[^}]*)\}");
            if (sourceTokens.Count == 0)
                continue;

            var pattern = new StringBuilder("^");
            var sourceOffset = 0;
            foreach (Match token in sourceTokens)
            {
                pattern.Append(Regex.Escape(sourceText[sourceOffset..token.Index]));
                pattern.Append($"(?<arg{token.Groups[1].Value}>.*?)");
                sourceOffset = token.Index + token.Length;
            }
            pattern.Append(Regex.Escape(sourceText[sourceOffset..]));
            pattern.Append('$');

            var candidate = Regex.Match(message, pattern.ToString(), RegexOptions.Singleline);
            if (!candidate.Success)
                continue;

            return Regex.Replace(targetText, @"\{(\d+)(?:[^}]*)\}", match =>
            {
                var groupName = $"arg{match.Groups[1].Value}";
                return candidate.Groups[groupName].Success ? candidate.Groups[groupName].Value : match.Value;
            });
        }

        var containsArabic = Regex.IsMatch(message, "[\\u0600-\\u06FF]");
        if ((CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase) && containsArabic) ||
            (!CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase) && !containsArabic))
            return message;

        return GetString("CommonUnexpectedError");
    }

    public FlowDirection GetFlowDirection()
    {
        return _currentCulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private LocalizationService()
    {
        // Load dictionaries from assembly
        _englishDict.Source = new Uri(
            "pack://application:,,,/AlphaLedger.App;component/Localization/en/Strings.xaml",
            UriKind.Absolute);
        _arabicDict.Source = new Uri(
            "pack://application:,,,/AlphaLedger.App;component/Localization/ar/Strings.xaml",
            UriKind.Absolute);

        ApplyCulture("en");
    }

    public void SetLanguage(string cultureName)
    {
        var normalizedCultureName = NormalizeCultureName(cultureName);
        ApplyCulture(normalizedCultureName);
        CultureChanged?.Invoke();
    }

    public static string NormalizeCultureName(string? cultureName)
    {
        return cultureName?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true ||
               cultureName?.Contains("العربية", StringComparison.Ordinal) == true
            ? "ar-EG"
            : "en-US";
    }

    private void ApplyCulture(string cultureName)
    {
        var baseCulture = CultureInfo.GetCultureInfo(NormalizeCultureName(cultureName));
        // Force English (Latin) digits and English date/number formats always,
        // even when the UI language is Arabic.
        var englishCulture = CultureInfo.GetCultureInfo("en-US");
        var culture = (CultureInfo)baseCulture.Clone();
        culture.NumberFormat = (NumberFormatInfo)englishCulture.NumberFormat.Clone();
        culture.DateTimeFormat = (DateTimeFormatInfo)englishCulture.DateTimeFormat.Clone();
        CurrentCulture = culture;

        var app = Application.Current;
        if (app == null) return;

        var merged = app.Resources.MergedDictionaries;

        // Remove existing string dictionaries if present
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var dict = merged[i];
            if (dict.Source != null &&
                (dict.Source.OriginalString.Contains("Localization/en/Strings.xaml") ||
                 dict.Source.OriginalString.Contains("Localization/ar/Strings.xaml")))
            {
                merged.RemoveAt(i);
            }
        }

        // Add the appropriate dictionary
        var dictToAdd = CurrentCulture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? _arabicDict : _englishDict;
        merged.Add(dictToAdd);

        foreach (Window window in app.Windows)
        {
            window.FlowDirection = GetFlowDirection();
            window.Language = XmlLanguage.GetLanguage("en-US");
        }
    }
}