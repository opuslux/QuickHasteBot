using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using QuickHasteBot.Interop;

namespace QuickHasteBot.UI;

internal sealed class TrayIcon : IDisposable
{
    readonly TaskbarIcon _icon;
    readonly ContextMenu _menu;
    readonly AppController _app;
    MenuItem? _updateItem;
    readonly ForegroundWatcher _foreground;
    nint _lastWindow;
    long _doubleClicked = long.MinValue / 2;

    /// <summary>So lange nach einem Doppelklick zählt ein Loslassen noch zu ihm (Millisekunden).</summary>
    const int DoubleClickGuard = 500;

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
        // Linksklick öffnet das Popup wie der Hotkey, Doppelklick die Einstellungen. Der zweite Klick eines Doppelklicks
        // öffnet das Popup nicht nochmals, es schliesst sich ohnehin, sobald die Einstellungen vorne sind.
        _icon.TrayMouseDoubleClick += (_, _) =>
        {
            _doubleClicked = Environment.TickCount64;
            app.ShowSettings();
        };
        _icon.TrayLeftMouseUp += (_, _) =>
        {
            if (Environment.TickCount64 - _doubleClicked > DoubleClickGuard)
                app.ShowSearchFromTray(LastWindow());
        };
        _lastWindow = NativeMethods.GetForegroundWindow();
        _foreground = new ForegroundWatcher(window =>
        {
            if (!IsShell(window)) _lastWindow = window;
        });
        _icon.TrayBalloonTipClicked += (_, _) => app.Updates.OpenPage();

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
        if (_app.Updates.Available is not { } tag)
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
            _updateItem = MenuIcons.Item(text, MenuIcons.Glyph(MenuIcons.Download), _app.Updates.OpenPage);
            _menu.Items.Insert(0, _updateItem);
            _menu.Items.Insert(1, new Separator());
        }
        else
            _updateItem.Header = text;
        if (notify)
            _icon.ShowNotification("QuickHasteBot", text + Environment.NewLine + Strings.UpdateNotificationText);
    }

    public void UpdateToolTip(string text) => _icon.ToolTipText = text;

    /// <summary>
    /// Wohin das Popup nach einem Klick aufs Tray-Symbol einfügt: das Fenster, das vorher vorne war. Beim Klick ist es die
    /// Taskleiste, darum merkt sich ein <see cref="ForegroundWatcher"/> laufend das letzte andere (eigene Fenster meldet er nicht).
    /// </summary>
    nint LastWindow() =>
        _lastWindow != 0 && NativeMethods.IsWindow(_lastWindow) && !IsShell(_lastWindow) ? _lastWindow : 0;

    /// <summary>Taskleiste, Infobereich mit seinem Überlauf und Startmenü: kein Ziel zum Einfügen.</summary>
    static bool IsShell(nint window) => WindowFinder.ClassName(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
        or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "XamlExplorerHostIslandWindow"
        or "Windows.UI.Core.CoreWindow";

    public void Dispose()
    {
        _foreground.Dispose();
        _icon.Dispose();
    }
}
