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
    AiPromptWindow? _aiPromptWindow;
    HelpWindow? _helpWindow;
    ElementPickerWindow? _elementPicker;
    readonly List<SnippetEditorWindow> _editors = [];

    // Datei-Hotkeys: was registriert ist, nach vollständigem Pfad, und was beim letzten Abgleich herauskam
    readonly Dictionary<string, (int Id, HotkeyGesture Gesture)> _fileRegistrations = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, FileHotkeyEntry> _fileHotkeyByPath = new(StringComparer.OrdinalIgnoreCase);
    HashSet<string> _knownConflicts = [];
    bool _hotkeysSuspended;

    public AppController(AppPaths paths)
    {
        _paths = paths;
        _store = new SettingsStore(paths.SettingsFile);
        Settings = LoadSettings();
        Log.Enabled = Settings.WriteLog;
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
        UiTheme.Apply(Settings.Theme);
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => ShowSearch();
        _hotkey.FilePressed += (_, id) => _ = OnFileHotkeyAsync(id);
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

        // Konflikte beim Start nicht als Meldung, sondern gleich dort, wo man sie sieht und beheben kann
        SyncFileHotkeys(announce: false);
        if (FileHotkeys.Any(e => e.Status != FileHotkeyStatus.Ok))
        {
            ShowSettings();
            _settingsWindow?.ShowFileHotkeys();
        }

        Log.Info($"Gestartet, Hotkey {ConfiguredHotkey} {(HotkeyActive ? "aktiv" : "belegt")}, {Library.All.Count} Einträge in {StructureRoot}, " +
                 $"{FileHotkeys.Count(e => e.Status == FileHotkeyStatus.Ok)} Datei-Hotkeys");
    }

    public void ShowSearch()
    {
        // Merken, wohin eingefügt werden soll, bevor das Popup den Fokus übernimmt
        var target = NativeMethods.GetForegroundWindow();
        // structure lässt sich jederzeit im Explorer umbauen: im Hintergrund prüfen, ein geänderter Baum
        // aktualisiert das offene Popup (Library.Changed)
        _ = Library.VerifyAsync();
        _popup?.ShowFor(target);
    }

    public void ShowSettings() => ShowSettings(position: null);

    /// <summary>Das KI-Prompt-Fenster gibt es nur einmal, ein weiterer Klick holt es nach vorne.</summary>
    public void ShowAiPrompt() => ShowAiPrompt(position: null);

    internal void ShowAiPrompt(Point? position)
    {
        if (_aiPromptWindow is null)
        {
            var window = new AiPromptWindow(this);
            if (position is { } p)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                (window.Left, window.Top) = (p.X, p.Y);
            }
            window.Closed += (_, _) =>
            {
                if (_aiPromptWindow == window)
                    _aiPromptWindow = null;
            };
            _aiPromptWindow = window;
            window.Show();
        }

        if (_aiPromptWindow.WindowState == WindowState.Minimized)
            _aiPromptWindow.WindowState = WindowState.Normal;
        _aiPromptWindow.Activate();
    }

    public void ShowHelp() => ShowHelp(anchor: null);

    /// <summary>Das README als Hilfe, nur ein Fenster. Mit <paramref name="anchor"/> an diesem Abschnitt, z.B. "editor".</summary>
    public void ShowHelp(string? anchor)
    {
        if (_helpWindow is null)
        {
            var window = new HelpWindow(ShellOpen);
            window.Closed += (_, _) =>
            {
                if (_helpWindow == window)
                    _helpWindow = null;
            };
            _helpWindow = window;
            window.Show();
        }

        if (anchor is not null)
            _helpWindow.ScrollTo(anchor);
        if (_helpWindow.WindowState == WindowState.Minimized)
            _helpWindow.WindowState = WindowState.Normal;
        _helpWindow.Activate();
    }

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
            Bring(open);
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
        Bring(window);
    }

    static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Nach dem Speichern im Editor: Liste gleich neu einlesen, nicht erst wenn der Watcher sich meldet.</summary>
    internal void SnippetSaved(string path)
    {
        Library.Reload();
        Log.Info($"Baustein gespeichert: {path}");
    }

    /// <summary>Den Element-Picker gibt es nur einmal, ein weiterer Aufruf holt ihn nach vorne.</summary>
    public void ShowElementPicker()
    {
        if (_elementPicker is null)
        {
            var window = new ElementPickerWindow(this);
            window.Closed += (_, _) =>
            {
                if (_elementPicker == window)
                    _elementPicker = null;
            };
            _elementPicker = window;
            window.Show();
        }

        if (_elementPicker.WindowState == WindowState.Minimized)
            _elementPicker.WindowState = WindowState.Normal;
        _elementPicker.Activate();
    }

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
            MessageDialog.Show(string.Format(Strings.SnippetReadErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await RunTextAsync(text, snippet.Name, alternate, targetWindow, snippet.FullPath) is { } problem)
            MessageDialog.Show(problem.Message, MessageBoxButton.OK, problem.Icon);
    }

    /// <summary>Warum ein Baustein nicht (ganz) eingefügt wurde, für eine Meldung.</summary>
    /// <param name="Step">Der Schritt (ab 0), bei dem es passiert ist, falls bekannt.</param>
    /// <param name="Stopped">Nur mit Step-Gate: der Lauf wurde gestoppt (Esc, Gate), kein Fehler. Ohne Gate gibt es dafür null.</param>
    internal sealed record RunProblem(string Message, MessageBoxImage Icon, int? Step = null, bool Stopped = false);

    /// <summary>
    /// Bringt einen Bausteintext ins Zielfenster: Tasten-Tags markieren, Skripte auswerten, dann als Formular-Baustein
    /// ausführen oder einfügen. Auch für den Testlauf im Editor, mit dem ungespeicherten Text.
    /// </summary>
    /// <param name="gate">Schritt für Schritt: bekommt die Schritte samt Stelle im Text und darf vor jedem anhalten.
    /// Ein Baustein ohne Tasten-Tags ist dabei ein einziger Schritt, der alles einfügt.</param>
    /// <returns>Null, wenn alles geklappt hat (oder ohne Gate mit Esc gestoppt wurde).</returns>
    internal async Task<RunProblem?> RunTextAsync(string text, string name, bool alternate, nint targetWindow, string? logPath = null,
        IStepGate? gate = null)
    {
        // Tasten-Tags werden zu Markern, bevor Skripte laufen: was ein Skript ausgibt oder aus der Zwischenablage kommt,
        // wird so nie als Tag gelesen, nur key() im Skript erzeugt bewusst Tasten
        text = KeyTags.StripMarkers(text);
        var sourceLength = text.Length;
        var form = Settings.EvaluateKeyTags ? new FormBuilder() : null;
        if (form is not null)
            text = KeyTags.MarkTags(text, form);

        using var session = Settings.EvaluateScripts && SnippetScript.HasBlocks(text)
            ? new ScriptSession(new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture,
                // Vor dem Einfügen lesen, PasteService legt danach den Baustein in die Zwischenablage
                SnippetScript.UsesClipboard(text) ? PasteService.TryGetClipboardText() : "",
                Form: form, Run: request => ScriptRunner.Run(request, PasteService.EscapeDown)))
            : null;
        if (session is not null)
        {
            try
            {
                // run() an seiner Stelle: Steht davor schon ein Tag, werden die Blöcke ab dem ersten run() erst
                // ausgewertet, wenn der Lauf dort ankommt. Sonst passiert vorher nichts, und alles läuft jetzt.
                if (form is not null && ScriptSession.NeedsStaging(text))
                    text = session.RenderStaged(text);
                else if (ScriptSession.UsesRun(text))
                    text = await Task.Run(() => session.Render(text)); // run() wartet, die Oberfläche nicht
                else
                    text = session.Render(text);
            }
            catch (SnippetScriptException ex)
            {
                // Lieber gar nichts einfügen als eine Fehlermeldung mitten in eine Mail
                Log.Error($"Skript in Block {ex.Block} fehlgeschlagen: {logPath ?? name}", ex);
                return new(string.Format(Strings.ScriptErrorFormat, ex.Block, name, ex.Message), MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
                return gate is null ? null : new("", MessageBoxImage.Information, 0, Stopped: true); // Esc beim Warten auf run()
            }
        }

        if (form is not null && KeyTags.HasMarkers(text))
        {
            var result = await _paste.RunFormAsync(KeyTags.SplitWithSources(text, form), targetWindow, Settings.KeyTagsWindowGuard, gate,
                session is null ? null : ExpandAsync);
            if (gate is not null && result.Outcome == FormOutcome.EscapePressed)
                return new("", MessageBoxImage.Information, result.StepIndex, Stopped: true);
            return FormMessage(result, name) is { } message ? new(message, MessageBoxImage.Information, result.StepIndex) : null;

            // Ein zurückgestellter Block, jetzt an der Reihe: auswerten (run() wartet im Hintergrund). Seine Schritte setzt
            // der Lauf dahinter ein.
            async Task<Expansion> ExpandAsync(DeferredStep deferred, SourceSpan source)
            {
                try
                {
                    return new Expansion(await Task.Run(() => session.Evaluate(deferred, source)));
                }
                catch (SnippetScriptException ex)
                {
                    Log.Error($"Skript in Block {ex.Block} fehlgeschlagen: {logPath ?? name}", ex);
                    return new Expansion([], string.Format(Strings.FormScriptFailedFormat, name, ex.Block, ex.Message));
                }
                catch (OperationCanceledException)
                {
                    return new Expansion([], Cancelled: true);
                }
            }
        }

        if (gate is not null)
        {
            gate.Planned([new SourcedStep(new TextStep(text), new SourceSpan(0, sourceLength))], paste: true);
            switch (await gate.BeforeStepAsync(0))
            {
                case StepDecision.Stop:
                    return new("", MessageBoxImage.Information, 0, Stopped: true);
                case StepDecision.RunAfterPause when !await PasteService.ResumeAsync(targetWindow):
                    return new(string.Format(Strings.FormWindowChangedFormat, name), MessageBoxImage.Information, 0);
            }
        }

        var mode = alternate ? AppSettings.Other(Settings.PasteMode) : Settings.PasteMode;
        if (!await _paste.PasteAsync(text, mode, targetWindow))
            return new(string.Format(Strings.TargetElevatedFormat, name, ProcessAccess.ProcessName(NativeMethods.GetForegroundWindow())),
                MessageBoxImage.Information, gate is null ? null : 0);
        return null;
    }

    /// <summary>
    /// Beginnt der Text mit {window,…} oder {start,…}, holt er sein Fenster selbst: dann dieses Tag, sonst null. Der
    /// Editor wartet dann nicht auf den Klick ins Zielfeld.
    /// </summary>
    internal string? StartWindowTag(string text) => StartWindowTag(text, Settings.EvaluateKeyTags, Settings.EvaluateScripts);

    /// <summary>Wie <see cref="StartWindowTag(string)"/>. Skripte sehen eine leere Zwischenablage, ein Skriptfehler gibt null.</summary>
    internal static string? StartWindowTag(string text, bool keyTags, bool scripts)
    {
        if (!keyTags) return null;
        var form = new FormBuilder();
        text = KeyTags.MarkTags(KeyTags.StripMarkers(text), form);
        if (scripts && SnippetScript.HasBlocks(text))
        {
            try
            {
                text = SnippetScript.Render(text, new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, "", Form: form));
            }
            catch (SnippetScriptException)
            {
                return null;
            }
        }
        return KeyTags.HasMarkers(text) && PasteService.StartWindowStep(KeyTags.Split(text, form)) is { } step ? KeyTags.Describe(step) : null;
    }

    /// <param name="Text">Was eingefügt würde, Tasten als lesbare Tags wie {tab}.</param>
    /// <param name="IsForm">Enthält Tasten- oder Element-Tags, wird darum getippt.</param>
    /// <param name="Error">Skriptfehler statt eines Ergebnisses.</param>
    internal sealed record SnippetPreview(string Text, bool IsForm, string? Error);

    /// <summary>
    /// Was ein Bausteintext ergibt, ohne etwas zu senden: für die Vorschau im Editor. Liest die Zwischenablage nur,
    /// wenn ein Skript sie erwähnt.
    /// </summary>
    internal SnippetPreview Preview(string text)
    {
        text = KeyTags.StripMarkers(text);
        var form = Settings.EvaluateKeyTags ? new FormBuilder() : null;
        if (form is not null)
            text = KeyTags.MarkTags(text, form);

        if (Settings.EvaluateScripts && SnippetScript.HasBlocks(text))
        {
            var clipboard = SnippetScript.UsesClipboard(text) ? PasteService.TryGetClipboardText() : "";
            try
            {
                // Ohne Run startet run() nichts. Blöcke ab dem ersten run() zeigt die Vorschau, wie sie dastehen.
                using var session = new ScriptSession(new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, clipboard, Form: form));
                text = form is not null && ScriptSession.UsesRun(text) ? session.RenderStaged(text) : session.Render(text);
            }
            catch (SnippetScriptException ex)
            {
                return new("", false, string.Format(Strings.AiPromptTestErrorFormat, ex.Block, ex.Message));
            }
        }

        return form is not null && KeyTags.HasMarkers(text)
            ? new(KeyTags.Preview(text, form), true, null)
            : new(text, false, null);
    }

    /// <summary>Warum ein Formular-Baustein abgebrochen hat, null wenn er durchgelaufen ist oder mit Esc gestoppt wurde.</summary>
    static string? FormMessage(FormResult result, string name) => result.Outcome switch
    {
        FormOutcome.WindowChanged => string.Format(Strings.FormWindowChangedFormat, name),
        FormOutcome.ElementNotFound => string.Format(Strings.FormElementNotFoundFormat, name, result.Step, ElementAutomation.FindTimeout.TotalSeconds),
        FormOutcome.ElementFailed => string.Format(Strings.FormElementFailedFormat, name, result.Step, result.Error),
        FormOutcome.WindowNotFound => string.Format(Strings.FormWindowNotFoundFormat, name, result.Step, ElementAutomation.FindTimeout.TotalSeconds),
        FormOutcome.WindowNotActivated => string.Format(Strings.FormWindowNotActivatedFormat, name, result.Step),
        FormOutcome.Elevated => string.Format(Strings.TargetElevatedFormat, name, result.Error),
        FormOutcome.StartFailed => string.Format(Strings.FormStartFailedFormat, name, result.Step, result.Error),
        FormOutcome.StartedWindowNotFound => string.Format(Strings.FormStartedWindowNotFoundFormat, name, result.Step, WindowFinder.StartTimeout.TotalSeconds),
        FormOutcome.ScriptFailed => result.Error,
        _ => null,
    };

    /// <summary>
    /// Für "Testen" im Element-Picker: führt einen Tag aus wie einen Baustein, im Fenster, in dem das Element erfasst
    /// wurde. Null heisst geklappt, sonst die Meldung, warum nicht.
    /// </summary>
    internal async Task<string?> TestTagAsync(string tag, nint targetWindow)
    {
        var form = new FormBuilder();
        var text = KeyTags.MarkTags(KeyTags.StripMarkers(tag), form);
        try
        {
            if (Settings.EvaluateScripts && SnippetScript.HasBlocks(text))
                text = SnippetScript.Render(text, new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, "", Form: form));
        }
        catch (SnippetScriptException ex)
        {
            return string.Format(Strings.ScriptErrorFormat, ex.Block, Strings.PickerTestName, ex.Message);
        }
        if (!KeyTags.HasMarkers(text))
            return Strings.PickerTestNoTag;

        var result = await _paste.RunFormAsync(KeyTags.SplitWithSources(text, form), targetWindow, Settings.KeyTagsWindowGuard);
        return result.Outcome switch
        {
            FormOutcome.Completed => null,
            FormOutcome.EscapePressed => Strings.PickerTestStopped,
            _ => FormMessage(result, Strings.PickerTestName),
        };
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
            ShellOpen(snippet.FullPath);
            return;
        }

        if (!TryStart(new ProcessStartInfo(snippet.FullPath) { UseShellExecute = true, Verb = "edit" }))
            ShellOpen(snippet.FullPath);
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
        var prompt = ScriptPrompt.Build(task, ReadAiPromptTemplate());
        ShellOpen(ScriptPrompt.Url(Settings.AiPromptUrl, prompt, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
    }

    /// <summary>Inhalt von ai-prompt.txt, null ohne Datei (dann gilt der eingebaute Prompt).</summary>
    public string? ReadAiPromptTemplate()
    {
        if (!File.Exists(_paths.AiPromptFile)) return null;
        try
        {
            return TextFileReader.Read(_paths.AiPromptFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht gelesen werden: {_paths.AiPromptFile}", ex);
            return null;
        }
    }

    /// <summary>Legt ai-prompt.txt beim ersten Mal mit dem eingebauten Prompt an und öffnet sie im Editor.</summary>
    public void EditAiPromptTemplate()
    {
        try
        {
            CreateAiPromptTemplate();
            ShellOpen(_paths.AiPromptFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht angelegt werden: {_paths.AiPromptFile}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptTemplateErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public bool HasAiPromptTemplate => File.Exists(_paths.AiPromptFile);

    /// <summary>Zurück zum eingebauten Prompt: ai-prompt.txt kommt in den Papierkorb, eigene Änderungen bleiben so wiederherstellbar.</summary>
    public void ResetAiPromptTemplate(nint owner)
    {
        if (!HasAiPromptTemplate) return;
        try
        {
            RecycleBin.Delete(_paths.AiPromptFile, owner);
        }
        catch (IOException ex)
        {
            Log.Error($"KI-Vorlage konnte nicht gelöscht werden: {_paths.AiPromptFile}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptResetErrorFormat, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <returns>Pfad der Vorlage. Eine vorhandene bleibt, wie sie ist.</returns>
    internal string CreateAiPromptTemplate()
    {
        if (!File.Exists(_paths.AiPromptFile))
            File.WriteAllText(_paths.AiPromptFile, ScriptPrompt.DefaultTemplate);
        return _paths.AiPromptFile;
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

    /// <summary>Solange eine Tastenkombination aufgenommen wird, darf kein Hotkey auslösen, auch keiner einer Datei.</summary>
    public void SuspendHotkey()
    {
        _hotkeysSuspended = true;
        _hotkey?.Unregister();
        foreach (var (id, _) in _fileRegistrations.Values)
            _hotkey?.Unregister(id);
        _fileRegistrations.Clear();
    }

    public void ResumeHotkey()
    {
        _hotkeysSuspended = false;
        // Zuerst die Dateien: eine Datei mit dem neuen Popup-Hotkey gibt ihn so frei
        SyncFileHotkeys();
        _hotkey?.Register(ConfiguredHotkey);
    }

    /// <summary>Alle Einträge mit Hotkey aus dem letzten Abgleich, in Baum-Reihenfolge.</summary>
    public IReadOnlyList<FileHotkeyEntry> FileHotkeys { get; private set; } = [];

    /// <summary>Nach jedem Abgleich, für die Liste in den Einstellungen.</summary>
    public event EventHandler? FileHotkeysChanged;

    /// <summary>
    /// Registriert die Hotkeys aus den Dateinamen neu: nach dem Einlesen, nach einer Änderung im Ordner und wenn sich
    /// der Popup-Hotkey ändert. Nur was sich geändert hat, wird frei gegeben oder registriert.
    /// </summary>
    /// <param name="announce">Neu entstandene Konflikte melden, z.B. nach Umbenennen im Explorer.</param>
    internal void SyncFileHotkeys(bool announce = true)
    {
        var entries = FileHotkeyMap.Build(Library.All, ConfiguredHotkey);
        // Ohne Hotkey-Fenster (vor Start, in Tests) nur die Liste
        if (_hotkey is not null && !_hotkeysSuspended)
        {
            var wanted = entries.Where(e => e.Status == FileHotkeyStatus.Ok)
                .ToDictionary(e => e.Snippet.FullPath, e => e.Gesture, StringComparer.OrdinalIgnoreCase);

            // Erst frei geben, damit ein Hotkey von einer Datei zur nächsten wandern kann
            foreach (var (path, registration) in _fileRegistrations.ToList())
            {
                if (wanted.TryGetValue(path, out var gesture) && gesture == registration.Gesture) continue;
                _hotkey.Unregister(registration.Id);
                _fileRegistrations.Remove(path);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Status != FileHotkeyStatus.Ok || _fileRegistrations.ContainsKey(entry.Snippet.FullPath)) continue;

                var id = FreeFileHotkeyId();
                if (_hotkey.Register(id, entry.Gesture))
                    _fileRegistrations[entry.Snippet.FullPath] = (id, entry.Gesture);
                else
                    entries[i] = entry with { Status = FileHotkeyStatus.Taken };
            }
        }

        FileHotkeys = entries;
        _fileHotkeyByPath = entries.ToDictionary(e => e.Snippet.FullPath, StringComparer.OrdinalIgnoreCase);

        // Nur Konflikte melden, die es vorher nicht gab: sonst kommt bei jeder Änderung im Ordner dieselbe Meldung
        var conflicts = entries.Where(e => e.Status != FileHotkeyStatus.Ok).ToList();
        var fresh = conflicts.Where(e => !_knownConflicts.Contains(ConflictKey(e))).ToList();
        _knownConflicts = conflicts.Select(ConflictKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        FileHotkeysChanged?.Invoke(this, EventArgs.Empty);

        foreach (var entry in fresh)
            Log.Info($"Datei-Hotkey {entry.Gesture} nicht aktiv ({entry.Status}): {entry.Snippet.FullPath}");
        if (announce && fresh.Count > 0)
            MessageDialog.Show(Strings.FileHotkeyConflictsFound + "\n\n" + string.Join("\n", fresh.Select(e => "• " + DescribeConflict(e))),
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    static string ConflictKey(FileHotkeyEntry entry) => $"{entry.Snippet.FullPath}|{entry.Gesture}|{entry.Status}";

    int FreeFileHotkeyId()
    {
        var used = _fileRegistrations.Values.Select(r => r.Id).ToHashSet();
        var id = GlobalHotkey.FirstFileId;
        while (used.Contains(id)) id++;
        return id;
    }

    /// <summary>"Ctrl+Alt+G für «Gruss»: Schon vergeben an «Grüsse» (1-Grüsse)."</summary>
    internal string DescribeConflict(FileHotkeyEntry entry) =>
        string.Format(Strings.FileHotkeyConflictLineFormat, entry.Gesture, DescribeSnippet(entry.Snippet), ConflictReason(entry));

    /// <summary>Warum der Hotkey nicht wirkt, null wenn er wirkt.</summary>
    internal static string? ConflictReason(FileHotkeyEntry entry) => entry.Status switch
    {
        FileHotkeyStatus.Duplicate => string.Format(Strings.FileHotkeyUsedByFormat, DescribeSnippet(entry.Owner!)),
        FileHotkeyStatus.MainHotkey => Strings.FileHotkeyIsMain,
        FileHotkeyStatus.Taken => Strings.FileHotkeyTaken,
        _ => null,
    };

    /// <summary>Für das Warnzeichen im Popup: warum der Hotkey dieses Eintrags nicht wirkt, null ohne Problem.</summary>
    internal string? HotkeyProblem(Snippet snippet) =>
        snippet.Hotkey is not null && _fileHotkeyByPath.TryGetValue(snippet.FullPath, out var entry) ? ConflictReason(entry) : null;

    static string DescribeSnippet(Snippet snippet) => snippet.Location.Length == 0
        ? string.Format(Strings.QuotedNameFormat, snippet.Name)
        : string.Format(Strings.QuotedNameWithLocationFormat, snippet.Name, snippet.Location);

    /// <summary>
    /// Ob ein Eintrag diesen Hotkey bekommen darf, für den Hotkey-Dialog und beim Umbenennen. Null wenn ja,
    /// sonst der Grund.
    /// </summary>
    /// <param name="selfPath">Der Eintrag selbst, sein bisheriger Hotkey ist natürlich kein Konflikt.</param>
    internal string? HotkeyConflict(HotkeyGesture gesture, string? selfPath)
    {
        if (gesture == ConfiguredHotkey) return Strings.FileHotkeyIsMain;
        if (FileHotkeyMap.FindOwner(Library.All, gesture, selfPath) is { } owner)
            return string.Format(Strings.FileHotkeyUsedByFormat, DescribeSnippet(owner));
        // Was wir selbst registriert haben, hält kein anderes Programm
        if (_fileRegistrations.Values.Any(r => r.Gesture == gesture)) return null;
        return _hotkey is not null && !_hotkey.IsFree(gesture) ? Strings.FileHotkeyTaken : null;
    }

    /// <summary>
    /// Beim Umbenennen: ob der Hotkey im neuen Namen frei ist. Null ohne Tag, mit dem bisherigen Hotkey oder einem freien.
    /// </summary>
    /// <param name="selfPath">Bisheriger Pfad des Eintrags, null bei einem neuen.</param>
    internal string? HotkeyConflictForName(string fileName, bool isFolder, string? selfPath, HotkeyGesture? currentHotkey)
    {
        if (FileHotkey.FromFileName(fileName, isFolder) is not { } gesture || gesture == currentHotkey) return null;
        return HotkeyConflict(gesture, selfPath);
    }

    /// <summary>Der Hotkey einer Datei oder eines Ordners: einfügen bzw. öffnen, beim Ordner das Popup dort.</summary>
    async Task OnFileHotkeyAsync(int id)
    {
        var path = _fileRegistrations.FirstOrDefault(r => r.Value.Id == id).Key;
        if (path is null) return;

        var target = NativeMethods.GetForegroundWindow();
        // Bei offenem Popup dorthin, wohin es eingefügt hätte, nicht ins Popup selbst
        if (_popup is { IsVisible: true } popup)
        {
            target = popup.TargetWindow;
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

        Library.Dispose();
        Library = CreateLibrary();
        _popup?.OnLibraryChanged();
        SyncFileHotkeys();
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

        // Der eingegebene Text überlebt das, er steht im Fenster statisch
        if (_aiPromptWindow is { } oldPrompt)
        {
            var position = new Point(oldPrompt.Left, oldPrompt.Top);
            _aiPromptWindow = null;
            oldPrompt.Close();
            ShowAiPrompt(position);
        }

        // Das README gibt es nur auf Englisch, nur der Titel wechselt
        if (_helpWindow is not null)
            _helpWindow.Title = Strings.HelpTitle;
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
        if (_aiPromptWindow is not null)
            SaveSettings();
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
            if (!ReferenceEquals(library, Library)) return;
            SyncFileHotkeys();
            _popup?.OnLibraryChanged();
        });
        library.StartWatching();
        _ = library.WarmUpAsync();
        return library;
    }

    /// <summary>
    /// Nur der Standardordner wird angelegt, ein konfigurierter Pfad (z.B. Netzlaufwerk) nicht.
    /// Neu angelegt kommen die Beispiele hinein, in der Sprache der Oberfläche.
    /// </summary>
    void EnsureStructureFolder(string root)
    {
        if (Directory.Exists(root) || Settings.StructurePath != AppSettings.DefaultStructurePath) return;

        try
        {
            Directory.CreateDirectory(root);
            SampleStructure.Extract(root, Strings.SampleLanguage);
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
