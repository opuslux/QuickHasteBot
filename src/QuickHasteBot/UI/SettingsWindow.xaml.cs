using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
        VersionText.Text = string.Format(Strings.VersionFormat, AppVersion.Display());
        ShowUpdate();
        HotkeyInput.Gesture = app.ConfiguredHotkey;
        PasteClipboard.IsChecked = app.Settings.PasteMode == PasteMode.Clipboard;
        PasteTyping.IsChecked = app.Settings.PasteMode == PasteMode.Typing;
        ClickSingle.IsChecked = app.Settings.SingleClick;
        ClickDouble.IsChecked = !app.Settings.SingleClick;
        RememberFolderInput.IsChecked = app.Settings.RememberFolder;
        RememberQueryInput.IsChecked = app.Settings.RememberQuery;
        PositionInput.Position = app.Settings.PopupPosition;
        StructureInput.Text = app.Settings.StructurePath;
        SearchContentInput.IsChecked = app.Settings.SearchContent;
        ScriptsInput.IsChecked = app.Settings.EvaluateScripts;
        BuiltInEditorInput.IsChecked = app.Settings.UseBuiltInEditor;
        KeyTagsInput.IsChecked = app.Settings.EvaluateKeyTags;
        WindowGuardInput.IsChecked = app.Settings.KeyTagsWindowGuard;
        (AiServiceInput.ItemsSource, AiServiceInput.SelectedItem) = app.AiServiceChoices();
        AutostartInput.IsChecked = Autostart.IsEnabled(AppController.ExePath);
        TrayInput.IsChecked = app.Settings.ShowTrayIcon;
        WriteLogInput.IsChecked = app.Settings.WriteLog;
        CheckForUpdatesInput.IsChecked = app.Settings.CheckForUpdates;
        LanguageInput.ItemsSource = UiLanguage.Choices;
        LanguageInput.SelectedItem = UiLanguage.Choices.FirstOrDefault(c => c.Code == app.Settings.Language) ?? UiLanguage.Choices[0];
        ThemeInput.ItemsSource = UiTheme.Choices;
        ThemeInput.SelectedItem = UiTheme.Choices.FirstOrDefault(c => c.Theme == app.Settings.Theme) ?? UiTheme.Choices[0];
        _loading = false;

        UpdateHotkeyStatus();
        UpdateFileHotkeys();
        app.FileHotkeys.Changed += OnFileHotkeysChanged;
        Closed += (_, _) => app.FileHotkeys.Changed -= OnFileHotkeysChanged;
        UpdateStructureResolved();
        UpdateLegacyHint();
        StartClip();

        // Stecknadel und Filter im Popup schalten dieselben Einstellungen, auch während dieses Fenster offen ist
        Activated += (_, _) =>
        {
            RememberFolderInput.IsChecked = _app.Settings.RememberFolder;
            RememberQueryInput.IsChecked = _app.Settings.RememberQuery;
        };

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
        // Als Referenz: folgt einem Wechsel zwischen hell und dunkel, solange das Fenster offen ist
        HotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
    }

    /// <summary>Beim Start mit Konflikten: gleich den Tab mit der Übersicht zeigen.</summary>
    public void ShowFileHotkeys() => FileHotkeysTab.IsSelected = true;

    /// <summary>Eine Zeile der Übersicht.</summary>
    internal sealed record FileHotkeyRow(Snippet Snippet, string Hotkey, string Name, string Location, string Status, bool IsProblem);

    void OnFileHotkeysChanged(object? sender, EventArgs e) => UpdateFileHotkeys();

    void UpdateFileHotkeys()
    {
        var rows = _app.FileHotkeys.Entries.Select(entry =>
        {
            var problem = FileHotkeyRegistry.ConflictReason(entry);
            return new FileHotkeyRow(entry.Snippet, entry.Gesture.ToString(), entry.Snippet.Name, entry.Snippet.Location,
                problem ?? Strings.FileHotkeyStatusOk, problem is not null);
        }).ToList();
        FileHotkeysList.ItemsSource = rows;

        var problems = rows.Count(r => r.IsProblem);
        FileHotkeysSummary.Text = rows.Count == 0 ? Strings.FileHotkeysNone
            : problems == 0 ? string.Format(Strings.FileHotkeysSummaryFormat, rows.Count)
            : string.Format(Strings.FileHotkeysProblemsFormat, rows.Count - problems, problems);
        FileHotkeysSummary.SetResourceReference(TextBlock.ForegroundProperty,
            problems > 0 ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
    }

    void OnFileHotkeyDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedFileHotkey();

    void OnFileHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        EditSelectedFileHotkey();
        e.Handled = true;
    }

    /// <summary>Wie "Hotkey festlegen…" im Popup: der Dialog liefert den neuen Namen, hier wird umbenannt.</summary>
    void EditSelectedFileHotkey()
    {
        if (FileHotkeysList.SelectedItem is not FileHotkeyRow row) return;

        var dialog = new HotkeyDialog(_app, row.Snippet) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SnippetFiles.Rename(row.Snippet.FullPath, dialog.NewFileName);
            _app.Library.Reload();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Konnte nicht umbenannt werden: {row.Snippet.FullPath}", ex);
            MessageDialog.Show(this, string.Format(Strings.RenameErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            HotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
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

    void OnRememberQueryChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.RememberQuery = RememberQueryInput.IsChecked == true;
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

    void OnBuiltInEditorChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.UseBuiltInEditor = BuiltInEditorInput.IsChecked == true;
        _app.SaveSettings();
    }

    void OnKeyTagsChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.EvaluateKeyTags = KeyTagsInput.IsChecked == true;
        _app.Settings.KeyTagsWindowGuard = WindowGuardInput.IsChecked == true;
        _app.SaveSettings();
    }

    void OnHelpClick(object sender, MouseButtonEventArgs e) => _app.ShowHelp();

    void OnProjectPageClick(object sender, MouseButtonEventArgs e) => AppController.OpenProjectPage();

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

    /// <summary>
    /// Vorgeschlagen wird der Ordner Demo im structure-Ordner. Gibt es ihn schon, wird gewarnt: Er kommt in den
    /// Papierkorb und wird neu aufgebaut, oder man wählt einen anderen Ordner. Dorthin kommen die Beispiele direkt,
    /// und steht darin schon etwas, werden nur gleichnamige Beispieldateien überschrieben.
    /// </summary>
    void OnExportSamples(object sender, MouseButtonEventArgs e)
    {
        var folder = _app.DemoRoot;
        var proposed = true;
        while (true)
        {
            var occupied = proposed
                ? Directory.Exists(folder)
                : Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
            // Ein selbst gewählter, leerer Ordner braucht keine Rückfrage mehr
            if (!occupied && !proposed) break;

            var (message, button) = (occupied, proposed) switch
            {
                (false, _) => (Strings.SamplesExportProposeFormat, Strings.SamplesExportButton),
                (true, true) => (Strings.SamplesRebuildFormat, Strings.SamplesRebuild),
                (true, false) => (Strings.SamplesExportOccupiedFormat, Strings.SamplesOverwrite),
            };
            var choice = MessageDialog.Show(this, string.Format(message, folder),
                occupied ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.Cancel,
                (MessageBoxResult.Yes, button),
                (MessageBoxResult.No, Strings.SamplesOtherFolder),
                (MessageBoxResult.Cancel, Strings.Cancel));
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.Yes) break;

            var dialog = new OpenFolderDialog
            {
                Title = Strings.SamplesFolderDialogTitle,
                InitialDirectory = Directory.Exists(folder) ? folder
                    : Directory.Exists(_app.StructureRoot) ? _app.StructureRoot : AppController.ExeDirectory,
            };
            // Abgebrochen: zurück zur Frage
            if (dialog.ShowDialog(this) != true) continue;
            folder = dialog.FolderName;
            proposed = false;
        }

        try
        {
            var count = _app.ExportSamples(folder, rebuild: proposed, new WindowInteropHelper(this).Handle);
            if (count is null) return;
            UpdateStructureResolved();
            MessageDialog.Show(this, string.Format(Strings.SamplesExportedFormat, count, folder), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Beispiele konnten nicht exportiert werden: {folder}", ex);
            MessageDialog.Show(this, string.Format(Strings.SamplesExportErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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

    void OnWriteLogChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.WriteLog = Log.Enabled = WriteLogInput.IsChecked == true;
        _app.SaveSettings();
        Log.Info("Log eingeschaltet");
    }

    void OnCheckForUpdatesChanged(object sender, RoutedEventArgs e) => _app.Updates.SetEnabled(CheckForUpdatesInput.IsChecked == true);

    /// <summary>Neben der Version: "Version 2.16.0 herunterladen", wenn es auf GitHub eine neuere gibt.</summary>
    internal void ShowUpdate()
    {
        var visible = _app.Updates.Available is not null ? Visibility.Visible : Visibility.Collapsed;
        UpdateLink.Visibility = UpdateSeparator.Visibility = visible;
        if (_app.Updates.Available is { } tag)
            UpdateLink.Text = string.Format(Strings.UpdateAvailableFormat, tag.TrimStart('v', 'V'));
    }

    void OnUpdateClick(object sender, MouseButtonEventArgs e) => _app.Updates.OpenPage();

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageInput.SelectedItem is not LanguageChoice choice || choice.Code == _app.Settings.Language) return;

        // Dieses Fenster wird dabei ersetzt, darum erst nach dem Auswahl-Ereignis
        Dispatcher.BeginInvoke(() => _app.ChangeLanguage(choice.Code));
    }

    void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeInput.SelectedItem is ThemeChoice choice && choice.Theme != _app.Settings.Theme)
            _app.ChangeTheme(choice.Theme);
    }

    void OnRemoveLegacyAutostart(object sender, RoutedEventArgs e)
    {
        Autostart.RemoveLegacyEntry();
        UpdateLegacyHint();
    }

    void UpdateLegacyHint() => LegacyHint.Visibility = Autostart.LegacyEntry is null ? Visibility.Collapsed : Visibility.Visible;

    void OnExit(object sender, RoutedEventArgs e) => _app.Exit();

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
