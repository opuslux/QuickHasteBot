using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace QuickHasteBot.UI;

internal sealed class TrayIcon : IDisposable
{
    readonly TaskbarIcon _icon;

    public TrayIcon(AppController app)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuIcons.Item(Strings.TraySettings, MenuIcons.Glyph(MenuIcons.Settings), app.ShowSettings));
        menu.Items.Add(MenuIcons.Item(Strings.TrayOpenStructure, MenuIcons.Glyph(MenuIcons.Explorer), app.OpenStructureFolder));
        menu.Items.Add(MenuIcons.Item(Strings.TrayElementPicker, MenuIcons.Crosshair(), app.ShowElementPicker));
        menu.Items.Add(MenuIcons.Item(Strings.TrayAiPrompt, MenuIcons.Sparkle(), app.ShowAiPrompt));
        menu.Items.Add(MenuIcons.Item(Strings.Help, MenuIcons.Glyph(MenuIcons.Help), app.ShowHelp));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuIcons.Item(Strings.TrayExit, MenuIcons.Glyph(MenuIcons.Exit), app.Exit));

        _icon = new TaskbarIcon
        {
            ToolTipText = $"QuickHasteBot ({app.ConfiguredHotkey})",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/QuickHasteBot;component/Assets/QuickHasteBot.ico")),
            ContextMenu = menu,
        };
        _icon.TrayMouseDoubleClick += (_, _) => app.ShowSettings();

        // false: kein "Efficiency Mode", der würde den Prozess drosseln und den Hotkey träge machen
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void UpdateToolTip(string text) => _icon.ToolTipText = text;

    public void Dispose() => _icon.Dispose();
}
