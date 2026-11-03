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
        menu.Items.Add(MenuItem(Strings.TraySettings, app.ShowSettings));
        menu.Items.Add(MenuItem(Strings.TrayOpenStructure, app.OpenStructureFolder));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.TrayExit, app.Exit));

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

    static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
