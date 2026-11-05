using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Ersatz für <see cref="MessageBox"/>: Die Meldung von Windows kennt kein dunkles Theme, dieses Fenster folgt dem
/// Fluent-Theme der App. Knöpfe, Symbol, Ton, Esc und Strg+C wie bei der Vorlage.
/// </summary>
internal partial class MessageDialog : Window
{
    readonly MessageBoxImage _icon;

    /// <summary>Gedrückter Knopf, bis dahin (und nach Esc oder dem X) das vorsichtige Ergebnis.</summary>
    internal MessageBoxResult Result { get; private set; }

    internal MessageDialog(string text, MessageBoxButton buttons, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        InitializeComponent();
        MessageText.Text = text;
        _icon = icon;
        ShowSymbol(icon);

        var results = Results(buttons);
        var cancel = CancelResult(buttons);
        if (!results.Contains(defaultResult))
            defaultResult = results[0];
        // Schliessen mit dem X zählt wie Esc
        Result = cancel;

        foreach (var result in results)
        {
            var button = new Button
            {
                Content = Label(result),
                MinWidth = 100,
                Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = result == defaultResult,
                IsCancel = result == cancel,
            };
            if (button.IsDefault)
            {
                button.SetResourceReference(StyleProperty, "AccentButtonStyle");
                Loaded += (_, _) => button.Focus();
            }
            button.Click += (_, _) =>
            {
                Result = result;
                Close();
            };
            Buttons.Children.Add(button);
        }

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopyText()));
        ContentRendered += (_, _) => PlaySound();
    }

    /// <summary>Wie <see cref="MessageBox.Show(Window, string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult)"/>.</summary>
    /// <param name="owner">Darüber zentriert. Ohne (oder noch nicht angezeigt) mitten auf dem Bildschirm, zuoberst und in der Taskleiste.</param>
    public static MessageBoxResult Show(Window? owner, string text, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dialog = new MessageDialog(text, buttons, icon, defaultResult);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            // Z.B. nach dem Einfügen: Dann ist ein anderes Programm vorne, die Meldung soll trotzdem zu sehen sein
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static MessageBoxResult Show(string text, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(null, text, buttons, icon, defaultResult);

    static MessageBoxResult[] Results(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
        MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
        MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
        _ => [MessageBoxResult.OK],
    };

    /// <summary>Was Esc und das X ergeben. Bei Ja/Nein, anders als bei Windows, das vorsichtige Nein.</summary>
    static MessageBoxResult CancelResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.OK,
    };

    static string Label(MessageBoxResult result) => result switch
    {
        MessageBoxResult.Yes => Strings.DialogYes,
        MessageBoxResult.No => Strings.DialogNo,
        MessageBoxResult.Cancel => Strings.Cancel,
        _ => Strings.DialogOk,
    };

    void ShowSymbol(MessageBoxImage icon)
    {
        // Information = Asterisk, Warning = Exclamation, Error = Hand = Stop: jeweils derselbe Wert
        var (glyph, brush) = icon switch
        {
            MessageBoxImage.Information => ("", "AccentTextFillColorPrimaryBrush"),
            MessageBoxImage.Question => ("", "AccentTextFillColorPrimaryBrush"),
            MessageBoxImage.Warning => ("", "SystemFillColorCautionBrush"),
            MessageBoxImage.Error => ("", "SystemFillColorCriticalBrush"),
            _ => ("", ""),
        };
        if (glyph.Length == 0) return;

        Symbol.Text = glyph;
        Symbol.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Symbol.Visibility = Visibility.Visible;
    }

    void PlaySound()
    {
        var sound = _icon switch
        {
            MessageBoxImage.Information => SystemSounds.Asterisk,
            MessageBoxImage.Question => SystemSounds.Question,
            MessageBoxImage.Warning => SystemSounds.Exclamation,
            MessageBoxImage.Error => SystemSounds.Hand,
            _ => null,
        };
        sound?.Play();
    }

    /// <summary>Wie bei Windows: Strg+C kopiert die Meldung, z.B. für eine Fehlermeldung.</summary>
    void CopyText()
    {
        try
        {
            Clipboard.SetText($"{Title}\r\n\r\n{MessageText.Text}");
        }
        catch (COMException ex)
        {
            // Zwischenablage gerade von einem anderen Programm belegt
            Log.Error("Meldung konnte nicht kopiert werden", ex);
        }
    }
}
