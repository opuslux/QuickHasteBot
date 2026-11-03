using System.Globalization;

namespace QuickHasteBot.UI;

/// <summary>
/// Sprache der Oberfläche. Texte stehen in Strings.resx (Englisch, auch für alle anderen Sprachen)
/// und Strings.de.resx. Texte aus dem Code wechseln sofort, XAML-Texte erst mit neu erzeugten Fenstern,
/// siehe <see cref="AppController.ChangeLanguage"/>.
/// </summary>
internal static class UiLanguage
{
    static CultureInfo? _windows;

    /// <summary>Wählbare Sprachen, jeweils in der eigenen Sprache benannt. Leerer Code = Windows.</summary>
    public static IReadOnlyList<LanguageChoice> Choices =>
    [
        new("", Strings.LanguageAuto),
        new("en", "English"),
        new("de", "Deutsch"),
    ];

    public static void Apply(string code)
    {
        // Die Sprache von Windows merken, bevor wir sie zum ersten Mal ändern, für den Wechsel zurück auf "Automatisch"
        _windows ??= CultureInfo.CurrentUICulture;

        var culture = _windows;
        if (!string.IsNullOrEmpty(code))
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                // Von Hand verschriebener Wert in settings.json: dann eben wie Windows
            }
        }

        if (Equals(culture, CultureInfo.CurrentUICulture)) return;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

/// <summary>Eintrag der Sprachauswahl, die ComboBox zeigt <see cref="Name"/>.</summary>
internal sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}
