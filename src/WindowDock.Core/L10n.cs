using System.Globalization;
using System.Resources;

namespace WindowDock;

internal static class L10n
{
    private static readonly ResourceManager resources = new("WindowDock.Resources.Strings", typeof(L10n).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo(ResolveLanguage(CultureInfo.CurrentUICulture.Name));
    internal static CultureInfo Culture => culture;
    internal static string Language => culture.Name;
    internal static IReadOnlyList<string> SupportedLanguages { get; } = Array.AsReadOnly(new[] { "en", "ru", "es", "vi", "zh-Hant", "zh-Hans", "th", "ja", "ko", "de", "tr" });
    internal static void Initialize(string? language = null)
    {
        // CurrentUICulture is the user's display language; InstalledUICulture can be the
        // original Windows installation language and is not the right preference here.
        var requested = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language;
        culture = CultureInfo.GetCultureInfo(ResolveLanguage(requested));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // Keep CurrentCulture intact: numeric and date formatting follow OS preferences.
    }
    internal static string ResolveLanguage(string? name)
    {
        var tag = (name ?? "").Replace('_', '-').ToLowerInvariant();
        if (tag == "zh" || tag.StartsWith("zh-"))
        {
            var parts = tag.Split('-');
            if (parts.Contains("hans")) return "zh-Hans";
            return parts.Any(part => part is "hant" or "tw" or "hk" or "mo" or "cht") ? "zh-Hant" : "zh-Hans";
        }
        var language = tag.Split('-')[0];
        return language is "en" or "ru" or "es" or "vi" or "th" or "ja" or "ko" or "de" or "tr" ? language : "en";
    }
    internal static string Get(string key) => resources.GetString(key, culture) ?? resources.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key;
    internal static string Format(string key, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
    internal static string Columns(int count)
    {
        var form = count == 1 ? "One" : "Other";
        if (Language == "ru")
            form = count % 10 == 1 && count % 100 != 11 ? "One" : count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) ? "Few" : "Other";
        return Format(form == "Other" ? "MainWindowCommands.Columns" : "Counts.Columns." + form, count);
    }
    internal static ResourceSet Catalog(string language, bool fallback = false) => resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, fallback) ?? throw new InvalidOperationException("Missing locale: " + language);
}