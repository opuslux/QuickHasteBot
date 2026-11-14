using System.Diagnostics;
using System.Globalization;
using System.Windows;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Hält alles zusammen: Einstellungen, Bibliothek, Hotkey, Popup, Tray und die Fenster. Was einen Baustein laufen lässt
/// (<see cref="Runner"/>), die Datei-Hotkeys (<see cref="FileHotkeys"/>) und die Update-Prüfung (<see cref="Updates"/>)
/// sind eigene Klassen. Alle öffentlichen Methoden laufen auf dem UI-Thread.
/// </summary>
internal sealed class AppController : IDisposable
{
    readonly AppPaths _paths;
    readonly SettingsStore _store;
    GlobalHotkey? _hotkey;
    SearchPopup? _popup;
    TrayIcon? _tray;
    readonly SingleWindow<SettingsWindow> _settingsWindow = new();
    readonly SingleWindow<AiPromptWindow> _aiPromptWindow = new();
    readonly SingleWindow<HelpWindow> _helpWindow = new();
    readonly SingleWindow<ElementPickerWindow> _elementPicker = new();
    readonly List<SnippetEditorWindow> _editors = [];

    public AppController(AppPaths paths)
    {
        _paths = paths;
        _store = new SettingsStore(paths.SettingsFile);
        Settings = LoadSettings();
        Log.Enabled = Settings.WriteLog;
        UiLanguage.Apply(Settings.Language);
        // Die Bibliothek meldet Änderungen erst später über den Dispatcher, die Datei-Hotkeys gibt es dann schon
        Library = CreateLibrary();
        Runner = new SnippetRunner(Settings, () => Library);
        FileHotkeys = new FileHotkeyRegistry(() => Library, () => ConfiguredHotkey);
        Updates = new UpdateNotifier(Settings, SaveSettings);
        Updates.Changed += notify =>
        {
            _tray?.ShowUpdate(notify);
            _settingsWindow.Current?.ShowUpdate();
        };
    }

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "QuickHasteBot.exe");

    public static string ExeDirectory => AppContext.BaseDirectory;

    public AppSettings Settings { get; }

    public SnippetLibrary Library { get; private set; }

    /// <summary>Bausteine einfügen oder tippen, Vorschau und Test.</summary>
    internal SnippetRunner Runner { get; }

    /// <summary>Die Hotkeys aus den Datei- und Ordnernamen.</summary>
    internal FileHotkeyRegistry FileHotkeys { get; }

    /// <summary>Der Hinweis auf neue Versionen.</summary>
    internal UpdateNotifier Updates { get; }

    public string StructureRoot => Library.RootPath;

    public HotkeyGesture ConfiguredHotkey =>
        HotkeyGesture.TryParse(Settings.Hotkey, out var gesture) ? gesture : HotkeyGesture.Default;

    public bool HotkeyActive => _hotkey?.Current is not null;

    public void Start()
    {
        UiTheme.Apply(Settings.Theme);
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => ShowSearch();
        _hotkey.FilePressed += (_, id) => _ = OnFileHotkeyAsync(id);
        FileHotkeys.Attach(_hotkey);
        _popup = new SearchPopup(this);

        if (Settings.ShowTrayIcon)
            _tray = new TrayIcon(this);

        if (!_hotkey.Register(ConfiguredHotkey))
        {
            // Typisch beim Umstieg: QuickPasteBot läuft noch und hält Ctrl+Shift+Y
            ShowSettings();
            _settingsWindow.Current?.ShowMessage(
                string.Format(Strings.HotkeyTakenAtStartFormat, ConfiguredHotkey));
        }

        // Konflikte beim Start nicht als Meldung, sondern gleich dort, wo man sie sieht und beheben kann
        FileHotkeys.Sync(announce: false);
        if (FileHotkeys.Entries.Any(e => e.Status != FileHotkeyStatus.Ok))
        {
            ShowSettings();
            _settingsWindow.Current?.ShowFileHotkeys();
        }

        Log.Info($"Gestartet, Hotkey {ConfiguredHotkey} {(HotkeyActive ? "aktiv" : "belegt")}, {Library.All.Count} Einträge in {StructureRoot}, " +
                 $"{FileHotkeys.Entries.Count(e => e.Status == FileHotkeyStatus.Ok)} Datei-Hotkeys");

        Updates.Start();
    }

    public void ShowSearch()
    {
        // Merken, wohin eingefügt werden soll, bevor das Popup den Fokus übernimmt
        var target = NativeMethods.GetForegroundWindow();
        // structure lässt sich jederzeit im Explorer umbauen: im Hintergrund prüfen, ein geänderter Baum
        // aktualisiert das offene Popup (Library.Changed)
        _ = Library.VerifyAsync();
        // Offen gelassen: nur zurück ins Suchfeld, Ordner und Stelle bleiben
        if (_popup is { KeepOpen: true, IsVisible: true } keptOpen)
            keptOpen.Refocus(target);
        else
            _popup?.ShowFor(target);
    }

    /// <summary>
    /// Linksklick aufs Tray-Symbol: öffnet das Popup wie der Hotkey. Es fügt in <paramref name="lastWindow"/> ein, das
    /// Fenster vor dem Klick (die Taskleiste ist keines). Ein offen gelassenes Popup kommt nach vorne, sein Ziel bleibt.
    /// </summary>
    public void ShowSearchFromTray(nint lastWindow)
    {
        if (_popup is { KeepOpen: true, IsVisible: true } keptOpen)
        {
            keptOpen.Refocus(0);
            return;
        }
        _ = Library.VerifyAsync();
        _popup?.ShowFor(lastWindow);
    }

    public void ShowSettings() => ShowSettings(position: null);

    internal void ShowSettings(Point? position) => _settingsWindow.Show(() => new SettingsWindow(this), position);

    /// <summary>Das KI-Prompt-Fenster gibt es nur einmal, ein weiterer Klick holt es nach vorne.</summary>
    public void ShowAiPrompt() => ShowAiPrompt(position: null);

    internal void ShowAiPrompt(Point? position) => _aiPromptWindow.Show(() => new AiPromptWindow(this), position);

    public void ShowHelp() => ShowHelp(anchor: null);

    /// <summary>Das README als Hilfe, nur ein Fenster. Mit <paramref name="anchor"/> an diesem Abschnitt, z.B. "editor".</summary>
    public void ShowHelp(string? anchor) => _helpWindow.Show(() => new HelpWindow(Shell.Open), beforeActivate: help =>
    {
        if (anchor is not null)
            help.ScrollTo(anchor);
    });

    /// <summary>Den Element-Picker gibt es nur einmal, ein weiterer Aufruf holt ihn nach vorne.</summary>
    public void ShowElementPicker() => _elementPicker.Show(() => new ElementPickerWindow(this));

    /// <summary>Der zuletzt benutzte offene Editor: Picker und KI-Fenster können dort einfügen.</summary>
    internal SnippetEditorWindow? ActiveEditor { get; private set; }

    /// <summary>Endung für neue Bausteine, die erste der Textendungen (Standard .txt).</summary>
    internal string NewSnippetExtension => Settings.TextExtensions.FirstOrDefault(e => e.StartsWith('.')) ?? ".txt";

    /// <summary>
    /// "Bearbeiten" im Popup: Textbausteine im eingebauten Editor (ausser in den Einstellungen abgeschaltet),
    /// alles andere wie bisher mit dem Programm, das Windows dafür vorsieht.
    /// </summary>
    public void EditSnippet(Snippet snippet)
    {
        if (snippet.Kind == SnippetKind.Text && Settings.UseBuiltInEditor)
            OpenEditor(snippet.FullPath);
        else
            Edit(snippet);
    }

    /// <summary>Eine Datei im eingebauten Editor, ist sie schon offen, kommt dieses Fenster nach vorne.</summary>
    public void OpenEditor(string path)
    {
        if (_editors.FirstOrDefault(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            SingleWindow.Bring(open);
            return;
        }

        SnippetEditorWindow window;
        try
        {
            window = SnippetEditorWindow.Open(this, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Textbaustein konnte nicht gelesen werden: {path}", ex);
            MessageDialog.Show(string.Format(Strings.SnippetReadErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Track(window);
    }

    /// <summary>Neuer, leerer Baustein in diesem Ordner, angelegt wird die Datei beim ersten Speichern.</summary>
    public void NewSnippet(Snippet folder) => Track(SnippetEditorWindow.New(this, folder.IsFolder ? folder.FullPath : Path.GetDirectoryName(folder.FullPath)!));

    void Track(SnippetEditorWindow window)
    {
        _editors.Add(window);
        ActiveEditor = window;
        window.Activated += (_, _) => ActiveEditor = window;
        window.Closed += (_, _) =>
        {
            _editors.Remove(window);
            if (ActiveEditor == window)
                ActiveEditor = _editors.LastOrDefault();
        };
        window.Show();
        SingleWindow.Bring(window);
    }

    /// <summary>Nach dem Speichern im Editor: Liste gleich neu einlesen, nicht erst wenn der Watcher sich meldet.</summary>
    internal void SnippetSaved(string path)
    {
        Library.Reload();
        Log.Info($"Baustein gespeichert: {path}");
    }

    public async Task ActivateAsync(Snippet snippet, bool alternate, nint targetWindow)
    {
        if (snippet.Kind != SnippetKind.Text)
        {
            Shell.Open(snippet.FullPath);
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
            MessageDialog.Show(string.Format(Strings.SnippetReadErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await Runner.RunTextAsync(text, snippet.Name, alternate, targetWindow, snippet.FullPath) is { } problem)
            MessageDialog.Show(problem.Message, MessageBoxButton.OK, problem.Icon);
    }

    /// <summary>
    /// Textbausteine mit dem Programm, das in Windows für die Endung eingestellt ist (z.B. Notepad++). Das Verb "edit"
    /// führt bei .txt fest zum Windows-Editor. Ausführbare Endungen wie .bat würden mit "open" aber laufen, dort und bei
    /// sonstigen Dateien darum "edit", ohne ein solches Verb die zugeordnete Anwendung.
    /// </summary>
    public void Edit(Snippet snippet)
    {
        if (snippet.IsFolder || snippet.Kind == SnippetKind.Text && !IsExecutable(snippet.FullPath))
        {
            Shell.Open(snippet.FullPath);
            return;
        }

        if (!Shell.TryStart(new ProcessStartInfo(snippet.FullPath) { UseShellExecute = true, Verb = "edit" }))
            Shell.Open(snippet.FullPath);
    }

    /// <summary>Endungen, die Windows ausführt statt öffnet (PATHEXT: .exe, .bat, .cmd, .vbs …).</summary>
    internal static bool IsExecutable(string path)
    {
        var extensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD;.VBS;.JS;.WSF";
        return extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Auswahl für Einstellungen und Prompt-Fenster. Eine von Hand eingetragene Adresse erscheint als eigener Eintrag
    /// und bleibt, bis man etwas anderes wählt.
    /// </summary>
    public (IReadOnlyList<AiService> Choices, AiService Selected) AiServiceChoices()
    {
        if (ScriptPrompt.ServiceFor(Settings.AiPromptUrl) is { } known)
            return (ScriptPrompt.Services, known);

        var custom = new AiService(Strings.AiServiceCustom, Settings.AiPromptUrl);
        return ([.. ScriptPrompt.Services, custom], custom);
    }

    public void ChangeAiService(AiService service)
    {
        if (Settings.AiPromptUrl == service.Url) return;
        Settings.AiPromptUrl = service.Url;
        SaveSettings();
    }

    /// <summary>Öffnet den Prompt für einen Skriptblock im Browser, Standard ChatGPT. Liest die Vorlage jedes Mal frisch.</summary>
    public void OpenAiPrompt(string task)
    {
        var prompt = ScriptPrompt.Build(task, PromptTemplates.Read(AiTemplate));
        Shell.Open(ScriptPrompt.Url(Settings.AiPromptUrl, prompt, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }

    /// <summary>Die eigene Vorlage für "Skriptblock mit KI", ai-prompt.txt mit {task}. Lesen usw. in <see cref="PromptTemplates"/>.</summary>
    internal PromptTemplate AiTemplate => new(_paths.AiPromptFile, ScriptPrompt.DefaultTemplate);

    /// <summary>Die eigene Vorlage für "HasteScript AI", hastescript-prompt.txt.</summary>
    internal PromptTemplate HasteScriptTemplate => new(_paths.HasteScriptPromptFile, HasteScriptPrompt.DefaultTemplate);

    public void RevealInExplorer(Snippet snippet)
    {
        if (snippet.IsFolder)
            Shell.Open(snippet.FullPath);
        else
            Shell.TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{snippet.FullPath}\"") { UseShellExecute = true });
    }

    public void OpenStructureFolder()
    {
        Directory.CreateDirectory(StructureRoot);
        Shell.Open(StructureRoot);
    }

    public void ChangeHotkey(HotkeyGesture gesture)
    {
        Settings.Hotkey = gesture.ToString();
        SaveSettings();
        _tray?.UpdateToolTip($"QuickHasteBot ({gesture})");
        // Registriert wird beim Verlassen des Feldes (ResumeHotkey), solange ist der Hotkey pausiert
    }

    /// <summary>Solange eine Tastenkombination aufgenommen wird, darf kein Hotkey auslösen, auch keiner einer Datei.</summary>
    public void SuspendHotkey()
    {
        _hotkey?.Unregister();
        FileHotkeys.Suspend();
    }

    public void ResumeHotkey()
    {
        // Zuerst die Dateien: eine Datei mit dem neuen Popup-Hotkey gibt ihn so frei
        FileHotkeys.Resume();
        _hotkey?.Register(ConfiguredHotkey);
    }

    /// <summary>Der Hotkey einer Datei oder eines Ordners: einfügen bzw. öffnen, beim Ordner das Popup dort.</summary>
    async Task OnFileHotkeyAsync(int id)
    {
        var path = FileHotkeys.PathFor(id);
        if (path is null) return;

        var target = NativeMethods.GetForegroundWindow();
        // Bei offenem Popup dorthin, wohin es eingefügt hätte, nicht ins Popup selbst.
        // Offen gelassen bleibt es dabei stehen bzw. kommt nach dem Einfügen wieder.
        SearchPopup? keptOpen = null;
        if (_popup is { IsVisible: true } popup)
        {
            target = popup.TargetWindow;
            if (popup.KeepOpen)
                keptOpen = popup;
            else
                popup.Hide();
        }

        var snippet = Library.All.FirstOrDefault(s => string.Equals(s.FullPath, path, StringComparison.OrdinalIgnoreCase));
        _ = Library.VerifyAsync();
        if (snippet is null)
        {
            Log.Info($"Datei-Hotkey für einen Eintrag, den es nicht mehr gibt: {path}");
            return;
        }

        Log.Info($"Datei-Hotkey {snippet.Hotkey}: {snippet.RelativePath}");
        try
        {
            if (snippet.IsFolder)
                _popup?.ShowFor(target, snippet);
            else if (keptOpen is not null)
                await keptOpen.RunHiddenAsync(() => ActivateAsync(snippet, alternate: false, target));
            else
                await ActivateAsync(snippet, alternate: false, target);
        }
        catch (Exception ex)
        {
            Log.Error($"Datei-Hotkey fehlgeschlagen: {path}", ex);
        }
    }

    public void ChangeStructurePath(string path)
    {
        Settings.StructurePath = path;
        SaveSettings();
        ReloadLibrary();
    }

    /// <summary>Der Ordner mit den Beispielen im structure-Ordner, den die Einstellungen neu aufbauen.</summary>
    public string DemoRoot => Path.Combine(StructureRoot, SampleStructure.DemoFolder);

    /// <summary>
    /// Die Beispiele (wieder) in einen Ordner schreiben, aus den Einstellungen. Mit <paramref name="rebuild"/> kommt der
    /// Ordner vorher in den Papierkorb, so entsteht er ganz neu, eigene Änderungen bleiben wiederherstellbar. Sonst
    /// werden nur gleichnamige Dateien ersetzt. Liegt der Ordner im structure-Ordner, gleich neu einlesen: Gab es den
    /// structure-Ordner vorher nicht, läuft dort noch kein Watcher.
    /// </summary>
    /// <returns>Anzahl geschriebener Dateien, null wenn bei der Nachfrage von Windows (z.B. Netzlaufwerk ohne
    /// Papierkorb) abgebrochen wurde.</returns>
    public int? ExportSamples(string folder, bool rebuild, nint owner)
    {
        if (rebuild && Directory.Exists(folder) && !RecycleBin.Delete(folder, owner))
            return null;

        Directory.CreateDirectory(folder);
        var count = SampleStructure.Extract(folder, Strings.SampleLanguage);
        Log.Info($"{count} Beispiele nach {folder} exportiert{(rebuild ? ", neu aufgebaut" : "")}");
        if (IsInside(folder, StructureRoot))
            ReloadLibrary();
        return count;
    }

    /// <summary>Ob <paramref name="path"/> der Ordner <paramref name="root"/> selbst ist oder darin liegt.</summary>
    static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    void ReloadLibrary()
    {
        Library.Dispose();
        Library = CreateLibrary();
        _popup?.OnLibraryChanged();
        FileHotkeys.Sync();
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

        if (_settingsWindow.Take() is { } old)
        {
            var position = new Point(old.Left, old.Top);
            old.Close();
            ShowSettings(position);
        }

        // Der eingegebene Text überlebt das, er steht im Fenster statisch
        if (_aiPromptWindow.Take() is { } oldPrompt)
        {
            var position = new Point(oldPrompt.Left, oldPrompt.Top);
            oldPrompt.Close();
            ShowAiPrompt(position);
        }

        // Das README gibt es nur auf Englisch, nur der Titel wechselt
        if (_helpWindow.Current is { } help)
            help.Title = Strings.HelpTitle;
    }

    /// <summary>Gilt sofort für alle offenen Fenster, anders als die Sprache muss nichts neu aufgebaut werden.</summary>
    public void ChangeTheme(AppTheme theme)
    {
        Settings.Theme = theme;
        SaveSettings();
        UiTheme.Apply(theme);
    }

    public void SaveSettings() => _store.Save(Settings);

    public void Exit()
    {
        // Text im offenen KI-Prompt-Fenster, der sonst erst beim Senden oder Schliessen gespeichert wird
        if (_aiPromptWindow.Current is not null)
            SaveSettings();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        Updates.Dispose();
        FileHotkeys.Attach(null);
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
            if (!ReferenceEquals(library, Library)) return;
            FileHotkeys.Sync();
            _popup?.OnLibraryChanged();
        });
        library.StartWatching();
        _ = library.WarmUpAsync();
        return library;
    }

    /// <summary>
    /// Nur der Standardordner wird angelegt, ein konfigurierter Pfad (z.B. Netzlaufwerk) nicht.
    /// Neu angelegt kommen die Beispiele in den Unterordner Demo, in der Sprache der Oberfläche.
    /// </summary>
    void EnsureStructureFolder(string root)
    {
        if (Directory.Exists(root) || Settings.StructurePath != AppSettings.DefaultStructurePath) return;

        try
        {
            Directory.CreateDirectory(root);
            SampleStructure.Extract(Path.Combine(root, SampleStructure.DemoFolder), Strings.SampleLanguage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"structure-Ordner konnte nicht angelegt werden: {root}", ex);
        }
    }

    public const string ProjectUrl = "https://github.com/opuslux/QuickHasteBot";

    /// <summary>Die Projektseite auf GitHub im Standardbrowser (Einstellungen, unter dem Titel).</summary>
    public static void OpenProjectPage() => Shell.Open(ProjectUrl);
}
