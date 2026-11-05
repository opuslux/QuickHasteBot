using System.Windows;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Hell, dunkel oder wie Windows, über das Fluent-Theme der ganzen App. Wechselt sofort auch in offenen Fenstern,
/// eigene Farben (z.B. <see cref="SnippetHighlighting"/>) folgen über die Theme-Ressourcen.
/// </summary>
internal static class UiTheme
{
    public static IReadOnlyList<ThemeChoice> Choices =>
    [
        new(AppTheme.System, Strings.ThemeSystem),
        new(AppTheme.Light, Strings.ThemeLight),
        new(AppTheme.Dark, Strings.ThemeDark),
    ];

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;

        var mode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            // Auch ein von Hand verschriebener Wert in settings.json
            _ => ThemeMode.System,
        };
        if (app.ThemeMode != mode)
            app.ThemeMode = mode;
    }
}

/// <summary>Eintrag der Theme-Auswahl, die ComboBox zeigt <see cref="Name"/>.</summary>
internal sealed record ThemeChoice(AppTheme Theme, string Name)
{
    public override string ToString() => Name;
}
