using System.Globalization;
using Handsfreely.App.Infrastructure;

namespace Handsfreely.App.Localization;

public static class L
{
    public static string Language { get; private set; } = Resolve(Settings.Current.Language);

    public static event Action? LanguageChanged;

    public static string T(string key)
    {
        if (!Strings.Table.TryGetValue(key, out var s)) return $"[{key}]";
        return Language == "ru" ? s.Ru : s.En;
    }

    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static void SetLanguage(string? language)
    {
        Settings.Current.Language = language;
        Settings.Current.Save();
        Language = Resolve(language);
        LanguageChanged?.Invoke();
    }

    private static string Resolve(string? language) =>
        language ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
}
