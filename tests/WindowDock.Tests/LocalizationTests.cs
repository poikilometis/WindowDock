using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static void Localizations()
    {
        var formatCulture = CultureInfo.CurrentCulture;
        try
        {
            var baseline = L10n.Catalog("en").Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
            Check(baseline.Count >= 297, "The English catalog is incomplete.");
            foreach (var language in L10n.SupportedLanguages)
            {
                // Disable fallback: a missing satellite must fail, not silently use English.
                var catalog = L10n.Catalog(language).Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
                Check(catalog.Keys.Order().SequenceEqual(baseline.Keys.Order()), "Mismatched resource keys: " + language);
                L10n.Initialize(language);
                Check(L10n.Language == language && CultureInfo.CurrentUICulture.Name == language, "UI culture was not applied: " + language);
                Check(CultureInfo.CurrentCulture.Equals(formatCulture), "UI language changed the user's number/date preferences.");
                foreach (var (key, value) in catalog)
                {
                    Check(!string.IsNullOrWhiteSpace(value), "Empty translation: " + language + "/" + key);
                    static string[] Tokens(string text) => Regex.Matches(text, @"\{(?:\d+|agentId)\}").Select(match => match.Value).Order().ToArray();
                    Check(Tokens(value).SequenceEqual(Tokens(baseline[key])), "Changed placeholders: " + language + "/" + key);
                    Check(L10n.Get(key) == value, "Wrong resource fallback: " + language + "/" + key);
                    _ = string.Format(CultureInfo.InvariantCulture, value.Replace("{agentId}", "launch-id"), "one", "two", "three", "four");
                }
                var config = AppConfig.Default();
                Check(config.Workspaces[0].Name == L10n.Get("Models.Agents"), "Default tab is not localized: " + language);
                config.Workspaces[0].Name = "My agents / 私の作業 / Мои агенты";
                L10n.Initialize("en"); config.Validate();
                Check(config.Workspaces[0].Name == "My agents / 私の作業 / Мои агенты", "A language change renamed user data.");
            }
            foreach (var (requested, expected) in new[] {
                ("en-US", "en"), ("en-GB", "en"), ("ru-RU", "ru"), ("es-MX", "es"), ("es-ES", "es"), ("vi-VN", "vi"),
                ("zh", "zh-Hans"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-Hans", "zh-Hans"), ("zh-Hans-HK", "zh-Hans"),
                ("zh-TW", "zh-Hant"), ("zh-HK", "zh-Hant"), ("zh-MO", "zh-Hant"), ("zh-Hant-TW", "zh-Hant"), ("zh_CHT", "zh-Hant"),
                ("th-TH", "th"), ("ja-JP", "ja"), ("ko-KR", "ko"), ("de-CH", "de"), ("tr-TR", "tr"), ("fr-FR", "en"), ("", "en"), ("invalid", "en") })
                Check(L10n.ResolveLanguage(requested) == expected, "Wrong language selection for " + requested);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("vi-VN"); L10n.Initialize();
            Check(L10n.Language == "vi", "OS UI language is not selected automatically.");
            L10n.Initialize("en"); Check(L10n.Columns(1) == "1 column" && L10n.Columns(2) == "2 columns", "English plural forms are wrong.");
            L10n.Initialize("ru"); Check(L10n.Columns(1) == "1 столбец" && L10n.Columns(2) == "2 столбца" && L10n.Columns(5) == "5 столбцов" && L10n.Columns(11) == "11 столбцов" && L10n.Columns(21) == "21 столбец", "Russian plural forms are wrong.");
            report.Add($"PASS {L10n.SupportedLanguages.Count} complete locale catalogs, resource lookup, format placeholders, OS language aliases, plural forms and user-name preservation.");
        }
        finally { L10n.Initialize("ru"); }
    }
}