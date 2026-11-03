using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Hält alles zusammen: Einstellungen, Bibliothek, Hotkey, Popup, Tray und Einstellungsfenster.
/// Alle öffentlichen Methoden laufen auf dem UI-Thread.
/// </summary>
internal sealed class AppController : IDisposable
{
    readonly AppPaths _paths;
    readonly SettingsStore _store;
    readonly PasteService _paste = new();
    GlobalHotkey? _hotkey;
    SearchPopup? _popup;
    TrayIcon? _tray;
    SettingsWindow? _settingsWindow;

    public AppController(AppPaths paths)
    {
        _paths = paths;
        _store = new SettingsStore(paths.SettingsFile);
        Settings = LoadSettings();
        UiLanguage.Apply(Settings.Language);
        Library = CreateLibrary();
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "QuickHasteBot.exe");

    public static string ExeDirectory => AppContext.BaseDirectory;

    public AppSettings Settings { get; }

    public SnippetLibrary Library { get; private set; }

    public string StructureRoot => Library.RootPath;

    public HotkeyGesture ConfiguredHotkey =>
        HotkeyGesture.TryParse(Settings.Hotkey, out var gesture) ? gesture : HotkeyGesture.Default;

    public bool HotkeyActive => _hotkey?.Current is not null;

    public void Start()
    {
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => ShowSearch();
        _popup = new SearchPopup(this);

        if (Settings.ShowTrayIcon)
            _tray = new TrayIcon(this);

        if (!_hotkey.Register(ConfiguredHotkey))
        {
            // Typisch beim Umstieg: QuickPasteBot läuft noch und hält Ctrl+Shift+Y
            ShowSettings();
            _settingsWindow?.ShowMessage(
                string.Format(Strings.HotkeyTakenAtStartFormat, ConfiguredHotkey));
        }

        Log.Info($"Gestartet, Hotkey {ConfiguredHotkey} {(HotkeyActive ? "aktiv" : "belegt")}, {Library.All.Count} Einträge in {StructureRoot}");
    }

    public void ShowSearch()
    {
        // Merken, wohin eingefügt werden soll, bevor das Popup den Fokus übernimmt
        var target = NativeMethods.GetForegroundWindow();
        _popup?.ShowFor(target);
    }

    public void ShowSettings() => ShowSettings(position: null);

    internal void ShowSettings(Point? position)
    {
        if (_settingsWindow is null)
        {
            var window = new SettingsWindow(this);
            if (position is { } p)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                (window.Left, window.Top) = (p.X, p.Y);
            }
            window.Closed += (_, _) =>
            {
                if (_settingsWindow == window)
                    _settingsWindow = null;
            };
            _settingsWindow = window;
            window.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public async Task ActivateAsync(Snippet snippet, bool alternate, nint targetWindow)
    {
        if (snippet.Kind != SnippetKind.Text)
        {
            ShellOpen(snippet.FullPath);
            return;
        }

        string text;
        try
        {
            text = SnippetLibrary.ReadText(snippet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Textbaustein konnte nicht gelesen werden: {snippet.FullPath}", ex);
            MessageBox.Show(string.Format(Strings.SnippetReadErrorFormat, ex.Message), "QuickHasteBot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Settings.EvaluateScripts && SnippetScript.HasBlocks(text))
        {
            // Vor dem Einfügen lesen, PasteService legt danach den Baustein in die Zwischenablage
            var clipboard = SnippetScript.UsesClipboard(text) ? PasteService.TryGetClipboardText() : "";
            try
            {
                text = SnippetScript.Render(text, new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, clipboard));
            }
            catch (SnippetScriptException ex)
            {
                // Lieber gar nichts einfügen als eine Fehlermeldung mitten in eine Mail
                Log.Error($"Skript in Block {ex.Block} fehlgeschlagen: {snippet.FullPath}", ex);
                MessageBox.Show(string.Format(Strings.ScriptErrorFormat, ex.Block, snippet.Name, ex.Message), "QuickHasteBot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var mode = alternate ? AppSettings.Other(Settings.PasteMode) : Settings.PasteMode;
        await _paste.PasteAsync(text, mode, targetWindow);
    }

    /// <summary>Text-Dateien im Editor öffnen, alles andere mit der zugeordneten Anwendung.</summary>
    public void Edit(Snippet snippet)
    {
        if (snippet.IsFolder)
        {
            ShellOpen(snippet.FullPath);
            return;
        }

        if (!TryStart(new ProcessStartInfo(snippet.FullPath) { UseShellExecute = true, Verb = "edit" }))
            ShellOpen(snippet.FullPath);
    }

    public void RevealInExplorer(Snippet snippet)
    {
        if (snippet.IsFolder)
            ShellOpen(snippet.FullPath);
        else
            TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{snippet.FullPath}\"") { UseShellExecute = true });
    }

    public void OpenStructureFolder()
    {
        Directory.CreateDirectory(StructureRoot);
        ShellOpen(StructureRoot);
    }

    public void ChangeHotkey(HotkeyGesture gesture)
    {
        Settings.Hotkey = gesture.ToString();
        SaveSettings();
        _tray?.UpdateToolTip($"QuickHasteBot ({gesture})");
        // Registriert wird beim Verlassen des Feldes (ResumeHotkey), solange ist der Hotkey pausiert
    }

    public void SuspendHotkey() => _hotkey?.Unregister();

    public void ResumeHotkey() => _hotkey?.Register(ConfiguredHotkey);

    public void ChangeStructurePath(string path)
    {
        Settings.StructurePath = path;
        SaveSettings();

        Library.Dispose();
        Library = CreateLibrary();
        _popup?.OnLibraryChanged();
    }

    public void SetTrayIconVisible(bool visible)
    {
        Settings.ShowTrayIcon = visible;
        SaveSettings();

        if (visible && _tray is null)
        {
            _tray = new TrayIcon(this);
        }
        else if (!visible && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    /// <summary>
    /// Ohne Neustart: XAML-Texte werden nur beim Erzeugen gelesen, darum Popup, Tray-Icon und
    /// Einstellungsfenster neu aufbauen. Das Einstellungsfenster öffnet sich an derselben Stelle wieder.
    /// </summary>
    public void ChangeLanguage(string code)
    {
        Settings.Language = code;
        SaveSettings();
        UiLanguage.Apply(code);

        if (_popup is not null)
        {
            _popup.Close();
            _popup = new SearchPopup(this);
        }

        if (_tray is not null)
        {
            _tray.Dispose();
            _tray = new TrayIcon(this);
        }

        if (_settingsWindow is { } old)
        {
            var position = new Point(old.Left, old.Top);
            _settingsWindow = null;
            old.Close();
            ShowSettings(position);
        }
    }

    public void SaveSettings() => _store.Save(Settings);

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        Library.Dispose();
        _hotkey = null;
        _tray = null;
    }

    AppSettings LoadSettings()
    {
        if (_store.Exists)
            return _store.Load();

        // Erster Start: settings.xml der alten Version übernehmen, falls daneben kopiert
        var settings = LegacySettingsImporter.TryImport(_paths.LegacySettingsFile) ?? new AppSettings();
        if (File.Exists(_paths.LegacySettingsFile))
            Log.Info($"Einstellungen aus {_paths.LegacySettingsFile} übernommen, Hotkey {settings.Hotkey}");
        _store.Save(settings);
        return settings;
    }

    SnippetLibrary CreateLibrary()
    {
        var root = _paths.ResolveStructure(Settings.StructurePath);
        EnsureStructureFolder(root);

        var library = new SnippetLibrary(root, Settings.TextExtensions);
        library.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(library, Library))
                _popup?.OnLibraryChanged();
        });
        library.StartWatching();
        _ = library.WarmUpAsync();
        return library;
    }

    /// <summary>Nur der Standardordner wird angelegt, ein konfigurierter Pfad (z.B. Netzlaufwerk) nicht.</summary>
    void EnsureStructureFolder(string root)
    {
        if (Directory.Exists(root) || Settings.StructurePath != AppSettings.DefaultStructurePath) return;

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, Strings.WelcomeFileName), Strings.WelcomeText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"structure-Ordner konnte nicht angelegt werden: {root}", ex);
        }
    }

    static void ShellOpen(string path) => TryStart(new ProcessStartInfo(path) { UseShellExecute = true });

    static bool TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Konnte nicht öffnen: {info.FileName} {info.Arguments}", ex);
            return false;
        }
    }
}
