using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace QuickHasteBot.UI;

/// <summary>Ein Symbol der Leiste: Zeichen aus Segoe Fluent Icons, Name als Tooltip, ausgegraut, wenn es nichts zu tun gibt.</summary>
internal sealed record MenuIconCommand(string Glyph, string Name, bool Enabled, Action Action);

/// <summary>
/// Die einfachen Befehle als Symbolleiste oben im Rechtsklick-Menü, wie im Kontextmenü von Windows 11: im Editor für
/// den Text, im Popup für Dateien und Ordner.
/// </summary>
internal static class MenuIconRow
{
    static readonly ResourceDictionary Resources = new()
    {
        Source = new Uri("pack://application:,,,/QuickHasteBot;component/UI/MenuIconRow.xaml"),
    };

    /// <summary>Ohne die Spalte für Symbole und Kürzel eines Eintrags, bündig mit den Symbolen darunter.</summary>
    static readonly ControlTemplate Template = CreateTemplate();

    /// <summary>Links die Befehle in <paramref name="left"/>, rechtsbündig die in <paramref name="right"/>.</summary>
    /// <remarks>Ein Klick schliesst das Menü, bevor die Aktion läuft.</remarks>
    public static MenuItem Create(ContextMenu menu, IEnumerable<MenuIconCommand> left, IEnumerable<MenuIconCommand>? right = null)
    {
        var leftPanel = Panel(menu, left);
        var rightPanel = Panel(menu, right ?? []);
        DockPanel.SetDock(rightPanel, Dock.Right);
        var icons = new DockPanel { LastChildFill = false, Children = { rightPanel, leftPanel }, Margin = new Thickness(6, 2, 6, 4) };
        return new MenuItem { Header = icons, StaysOpenOnClick = true, Focusable = false, Template = Template };
    }

    static StackPanel Panel(ContextMenu menu, IEnumerable<MenuIconCommand> commands)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var command in commands)
        {
            var button = new Button
            {
                Content = command.Glyph, Style = (Style)Resources["MenuIconButton"], IsEnabled = command.Enabled, ToolTip = command.Name,
            };
            AutomationProperties.SetName(button, command.Name);
            button.Click += (_, _) =>
            {
                menu.IsOpen = false;
                command.Action();
            };
            panel.Children.Add(button);
        }
        return panel;
    }

    static ControlTemplate CreateTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter };
        template.Seal();
        return template;
    }
}
