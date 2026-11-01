using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>Änderungen gelten sofort und werden direkt gespeichert, wie in der alten Version.</summary>
internal partial class SettingsWindow : Window
{
    readonly AppController _app;
    readonly bool _loading;

    public SettingsWindow(AppController app)
    {
        InitializeComponent();
        _app = app;

        _loading = true;
        VersionText.Text = "Version " + DisplayVersion();
        HotkeyInput.Gesture = app.ConfiguredHotkey;
        PasteClipboard.IsChecked = app.Settings.PasteMode == PasteMode.Clipboard;
        PasteTyping.IsChecked = app.Settings.PasteMode == PasteMode.Typing;
        StructureInput.Text = app.Settings.StructurePath;
        SearchContentInput.IsChecked = app.Settings.SearchContent;
        AutostartInput.IsChecked = Autostart.IsEnabled(AppController.ExePath);
        TrayInput.IsChecked = app.Settings.ShowTrayIcon;
        _loading = false;

        UpdateHotkeyStatus();
        UpdateStructureResolved();
        UpdateLegacyHint();

        // Solange der Hotkey aufgenommen wird, darf er nicht das Popup öffnen
        HotkeyInput.GotKeyboardFocus += (_, _) => app.SuspendHotkey();
        HotkeyInput.LostKeyboardFocus += (_, _) =>
        {
            app.ResumeHotkey();
            UpdateHotkeyStatus();
        };
    }

    public void ShowMessage(string message)
    {
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
    }

    void OnHotkeyCaptured(object? sender, HotkeyGesture gesture) => _app.ChangeHotkey(gesture);

    void UpdateHotkeyStatus()
    {
        if (_app.HotkeyActive)
        {
            HotkeyStatus.Text = "Aktiv. Klicken und neue Kombination drücken, um sie zu ändern.";
            HotkeyStatus.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
        }
        else
        {
            ShowMessage($"{_app.ConfiguredHotkey} ist bereits von einem anderen Programm belegt. Bitte eine andere Kombination wählen.");
        }
    }

    void OnPasteModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.PasteMode = PasteTyping.IsChecked == true ? PasteMode.Typing : PasteMode.Clipboard;
        _app.SaveSettings();
    }

    void OnSearchContentChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.SearchContent = SearchContentInput.IsChecked == true;
        _app.SaveSettings();
    }

    void OnStructureInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitStructurePath();
            e.Handled = true;
        }
    }

    void OnStructureInputCommitted(object sender, KeyboardFocusChangedEventArgs e) => CommitStructurePath();

    void CommitStructurePath()
    {
        var value = StructureInput.Text.Trim();
        if (value.Length == 0)
            StructureInput.Text = value = AppSettings.DefaultStructurePath;
        if (value == _app.Settings.StructurePath) return;

        _app.ChangeStructurePath(value);
        UpdateStructureResolved();
    }

    void OnBrowseStructure(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ordner mit den Textbausteinen wählen",
            InitialDirectory = Directory.Exists(_app.StructureRoot) ? _app.StructureRoot : AppController.ExeDirectory,
        };
        if (dialog.ShowDialog(this) != true) return;

        // Liegt der Ordner unter dem exe-Ordner, relativ speichern, damit die App portable bleibt
        var relative = Path.GetRelativePath(AppController.ExeDirectory, dialog.FolderName);
        StructureInput.Text = relative.StartsWith("..") || Path.IsPathRooted(relative) ? dialog.FolderName : relative;
        CommitStructurePath();
    }

    void OnOpenStructure(object sender, RoutedEventArgs e) => _app.OpenStructureFolder();

    void UpdateStructureResolved()
    {
        var root = _app.StructureRoot;
        StructureResolved.Text = Directory.Exists(root)
            ? $"{root}  ({_app.Library.All.Count(s => !s.IsFolder)} Dateien)"
            : $"{root}  (existiert nicht)";
    }

    void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (AutostartInput.IsChecked == true)
            Autostart.Enable(AppController.ExePath);
        else
            Autostart.Disable();
    }

    void OnTrayChanged(object sender, RoutedEventArgs e) => _app.SetTrayIconVisible(TrayInput.IsChecked == true);

    void OnRemoveLegacyAutostart(object sender, RoutedEventArgs e)
    {
        Autostart.RemoveLegacyEntry();
        UpdateLegacyHint();
    }

    void UpdateLegacyHint() => LegacyHint.Visibility = Autostart.LegacyEntry is null ? Visibility.Collapsed : Visibility.Visible;

    void OnExit(object sender, RoutedEventArgs e) => _app.Exit();

    void OnClose(object sender, RoutedEventArgs e) => Close();

    static string DisplayVersion()
    {
        // Das SDK hängt den Commit an ("2.0.0+abc123…"), der kurze Hash reicht
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var plus = version.IndexOf('+');
        return plus < 0 ? version : $"{version[..plus]} ({version[(plus + 1)..Math.Min(version.Length, plus + 8)]})";
    }
}
