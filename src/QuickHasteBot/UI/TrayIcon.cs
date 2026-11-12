using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace QuickHasteBot.UI;

internal sealed class TrayIcon : IDisposable
{
    readonly TaskbarIcon _icon;
    readonly ContextMenu _menu;
    readonly AppController _app;
    MenuItem? _updateItem;

    public TrayIcon(AppController app)
    {
        _app = app;
        var menu = _menu = new ContextMenu();
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
        _icon.TrayLeftMouseUp += (_, _) => app.BringKeptOpenPopupToFront();
        _icon.TrayBalloonTipClicked += (_, _) => app.OpenUpdatePage();

        // false: kein "Efficiency Mode", der würde den Prozess drosseln und den Hotkey träge machen
        _icon.ForceCreate(enablesEfficiencyMode: false);
        ShowUpdate(notify: false);
    }

    /// <summary>
    /// Eine neue Version: oben im Menü ein Eintrag, der zur Download-Seite führt, und mit <paramref name="notify"/> eine
    /// Mitteilung von Windows. Ohne neue Version nichts.
    /// </summary>
    public void ShowUpdate(bool notify)
    {
        if (_app.AvailableUpdate is not { } tag)
        {
            // Abgeschaltet: Eintrag und Trenner wieder weg
            if (_updateItem is not null)
            {
                _menu.Items.RemoveAt(1);
                _menu.Items.Remove(_updateItem);
                _updateItem = null;
            }
            return;
        }
        var text = string.Format(Strings.UpdateAvailableFormat, tag.TrimStart('v', 'V'));
        if (_updateItem is null)
        {
            _updateItem = MenuIcons.Item(text, MenuIcons.Glyph(MenuIcons.Download), _app.OpenUpdatePage);
            _menu.Items.Insert(0, _updateItem);
            _menu.Items.Insert(1, new Separator());
        }
        else
            _updateItem.Header = text;
        if (notify)
            _icon.ShowNotification("QuickHasteBot", text + Environment.NewLine + Strings.UpdateNotificationText);
    }

    public void UpdateToolTip(string text) => _icon.ToolTipText = text;

    public void Dispose() => _icon.Dispose();
}
