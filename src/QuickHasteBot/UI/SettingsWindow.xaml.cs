using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
        VersionText.Text = string.Format(Strings.VersionFormat, DisplayVersion());
        HotkeyInput.Gesture = app.ConfiguredHotkey;
        PasteClipboard.IsChecked = app.Settings.PasteMode == PasteMode.Clipboard;
        PasteTyping.IsChecked = app.Settings.PasteMode == PasteMode.Typing;
        ClickSingle.IsChecked = app.Settings.SingleClick;
        ClickDouble.IsChecked = !app.Settings.SingleClick;
        RememberFolderInput.IsChecked = app.Settings.RememberFolder;
        PositionInput.Position = app.Settings.PopupPosition;
        StructureInput.Text = app.Settings.StructurePath;
        SearchContentInput.IsChecked = app.Settings.SearchContent;
        ScriptsInput.IsChecked = app.Settings.EvaluateScripts;
        (AiServiceInput.ItemsSource, AiServiceInput.SelectedItem) = app.AiServiceChoices();
        AutostartInput.IsChecked = Autostart.IsEnabled(AppController.ExePath);
        TrayInput.IsChecked = app.Settings.ShowTrayIcon;
        LanguageInput.ItemsSource = UiLanguage.Choices;
        LanguageInput.SelectedItem = UiLanguage.Choices.FirstOrDefault(c => c.Code == app.Settings.Language) ?? UiLanguage.Choices[0];
        _loading = false;

        UpdateHotkeyStatus();
        UpdateStructureResolved();
        UpdateLegacyHint();
        StartClip();

        // Die Stecknadel im Popup schaltet dieselbe Einstellung, auch während dieses Fenster offen ist
        Activated += (_, _) => RememberFolderInput.IsChecked = _app.Settings.RememberFolder;

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

    // Ein Klick auf einen Tab schiebt in WPF die Tastatur ins erste Feld des Tabs. Im ersten Tab ist das der Hotkey,
    // der dann auf eine Tastenkombination wartet und ausgesetzt ist. Darum nur umschalten.
    void OnTabsMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SettingsTabs, (DependencyObject)e.OriginalSource) is not TabItem tab) return;
        tab.IsSelected = true;
        e.Handled = true;
    }

    void OnHotkeyCaptured(object? sender, HotkeyGesture gesture) => _app.ChangeHotkey(gesture);

    void UpdateHotkeyStatus()
    {
        if (_app.HotkeyActive)
        {
            HotkeyStatus.Text = Strings.HotkeyActive;
            HotkeyStatus.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
        }
        else
        {
            ShowMessage(string.Format(Strings.HotkeyTakenFormat, _app.ConfiguredHotkey));
        }
    }

    void StartClip()
    {
        try
        {
            RoboClip.Source = new Uri(ExtractClip());
            RoboClip.Play();
            Closed += (_, _) => RoboClip.Close();
        }
        catch (Exception ex)
        {
            Log.Error("Video konnte nicht geladen werden", ex);
            RoboClip.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Nur neu schreiben, wenn die Datei fehlt oder von einer anderen Version stammt.</summary>
    static string ExtractClip()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoboClip.mp4")
            ?? throw new InvalidOperationException("Ressource RoboClip.mp4 fehlt");
        var path = Path.Combine(Path.GetTempPath(), "QuickHasteBot", "RoboClip.mp4");
        var existing = new FileInfo(path);
        if (existing.Exists && existing.Length == resource.Length) return path;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        resource.CopyTo(file);
        return path;
    }

    void OnClipEnded(object sender, RoutedEventArgs e)
    {
        RoboClip.Position = TimeSpan.Zero;
        RoboClip.Play();
    }

    // z.B. Windows N ohne Media Feature Pack: dann eben ohne Video
    void OnClipFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        Log.Error("Video kann nicht abgespielt werden", e.ErrorException);
        RoboClip.Visibility = Visibility.Collapsed;
    }

    void OnClipSizeChanged(object sender, SizeChangedEventArgs e) =>
        RoboClip.Clip = new RectangleGeometry(new Rect(e.NewSize), 8, 8);

    void OnPasteModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.PasteMode = PasteTyping.IsChecked == true ? PasteMode.Typing : PasteMode.Clipboard;
        _app.SaveSettings();
    }

    void OnClickModeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.Settings.SingleClick = ClickSingle.IsChecked == true;
        _app.SaveSettings();
    }

    void OnRememberFolderChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.RememberFolder = RememberFolderInput.IsChecked == true;
        _app.SaveSettings();
    }

    void OnPositionChanged(object? sender, PopupPosition position)
    {
        _app.Settings.PopupPosition = position;
        _app.SaveSettings();
    }

    void OnSearchContentChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.SearchContent = SearchContentInput.IsChecked == true;
        _app.SaveSettings();
    }

    void OnAiServiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && AiServiceInput.SelectedItem is AiService service)
            _app.ChangeAiService(service);
    }

    void OnScriptsChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.EvaluateScripts = ScriptsInput.IsChecked == true;
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
            Title = Strings.StructureDialogTitle,
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
            ? string.Format(Strings.StructureFilesFormat, root, _app.Library.All.Count(s => !s.IsFolder))
            : string.Format(Strings.StructureMissingFormat, root);
    }

    void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (AutostartInput.IsChecked == true)
            Autostart.Enable(AppController.ExePath);
        else
            Autostart.Disable();
    }

    void OnTrayChanged(object sender, RoutedEventArgs e) => _app.SetTrayIconVisible(TrayInput.IsChecked == true);

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageInput.SelectedItem is not LanguageChoice choice || choice.Code == _app.Settings.Language) return;

        // Dieses Fenster wird dabei ersetzt, darum erst nach dem Auswahl-Ereignis
        Dispatcher.BeginInvoke(() => _app.ChangeLanguage(choice.Code));
    }

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
