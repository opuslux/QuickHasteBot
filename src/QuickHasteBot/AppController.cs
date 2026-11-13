using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
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

        // Stündlich nachsehen, ob der Tag um ist: läuft das Programm wochenlang, kommt der Hinweis trotzdem
        _updateTimer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = CheckForUpdatesAsync(),
            Dispatcher.CurrentDispatcher);
        _ = CheckForUpdatesAsync();
    }

    // --- Neue Version ---

    static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    DispatcherTimer? _updateTimer;
    bool _checkingForUpdates;

    /// <summary>Das neuere Release auf GitHub, z.B. "v2.16.0", null wenn diese Version aktuell ist oder nicht gefragt wird.</summary>
    internal string? AvailableUpdate =>
        Settings.CheckForUpdates && UpdateCheck.IsNewer(Settings.LatestRelease, AppVersion.Informational) ? Settings.LatestRelease : null;

    internal void OpenUpdatePage()
    {
        if (AvailableUpdate is { } tag)
            ShellOpen(UpdateCheck.ReleasePage(tag));
        else
            OpenProjectPage();
    }

    internal void SetCheckForUpdates(bool on)
    {
        Settings.CheckForUpdates = on;
        SaveSettings();
        _tray?.ShowUpdate(notify: false);
        _settingsWindow?.ShowUpdate();
        if (on)
            _ = CheckForUpdatesAsync();
    }

    /// <summary>
    /// Fragt GitHub, wenn es dran ist (einmal am Tag). Eine Version, die neu dazukommt, meldet der Tray einmal; der Eintrag
    /// im Menü und der Link in den Einstellungen bleiben bis zum Update. Fehler (kein Netz) stehen nur im Log.
    /// </summary>
    internal async Task CheckForUpdatesAsync()
    {
        if (_checkingForUpdates || !Settings.CheckForUpdates || AppVersion.Numeric(AppVersion.Informational) is null
            || !UpdateCheck.Due(Settings.LastUpdateCheck, DateTimeOffset.Now))
            return;
        _checkingForUpdates = true;
        try
        {
            var release = await UpdateCheck.FetchAsync(UpdateClient);
            Settings.LastUpdateCheck = DateTimeOffset.Now;
            var known = Settings.LatestRelease;
            if (release is not null)
                Settings.LatestRelease = release.Tag;
            SaveSettings();
            if (AvailableUpdate is { } tag && !string.Equals(tag, known, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"Neue Version auf GitHub: {tag}");
                _tray?.ShowUpdate(notify: true);
                _settingsWindow?.ShowUpdate();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Keine Antwort von GitHub zur neuesten Version: {ex.Message}");
        }
        finally
        {
            _checkingForUpdates = false;
        }
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
    /// Linksklick aufs Tray-Symbol: Ein offen gelassenes Popup kommt nach vorne. Das Ziel bleibt, die Taskleiste ist
    /// keines. Ein geschlossenes Popup öffnet es nicht, dafür gibt es den Hotkey.
    /// </summary>
    public void BringKeptOpenPopupToFront()
    {
        if (_popup is { KeepOpen: true, IsVisible: true } keptOpen)
            keptOpen.Refocus(0);
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
    /// <param name="folder">Wo run() relative Pfade zuerst sucht; ohne Angabe der Ordner von <paramref name="logPath"/>.</param>
    internal async Task<RunProblem?> RunTextAsync(string text, string name, bool alternate, nint targetWindow, string? logPath = null,
        IStepGate? gate = null, string? folder = null)
    {
        folder ??= logPath is null ? null : Path.GetDirectoryName(logPath);
        // Tasten-Tags werden zu Markern, bevor Skripte laufen: was ein Skript ausgibt oder aus der Zwischenablage kommt,
        // wird so nie als Tag gelesen, nur key() im Skript erzeugt bewusst Tasten. Eingebundene Bausteine davor, ihre Tags
        // gelten so wie die eigenen.
        text = KeyTags.StripMarkers(text);
        var sourceLength = text.Length;
        var form = Settings.EvaluateKeyTags ? new FormBuilder() : null;
        if (form is not null)
        {
            try
            {
                text = SnippetIncludes.Expand(text, ResolveInclude, form, logPath);
            }
            catch (IncludeException ex)
            {
                return new(string.Format(Strings.IncludeErrorFormat, name, IncludeMessage(ex)), MessageBoxImage.Warning);
            }
            text = KeyTags.MarkTags(text, form);
        }

        // {if,js="…"} braucht die Engine auch ohne {{ … }} im Text
        var conditions = form is not null && form.Steps.Any(s => s is IfStep { Condition.Script: not null });
        var scripts = Settings.EvaluateScripts && (SnippetScript.HasBlocks(text) || conditions);
        // Vor dem Einfügen lesen, PasteService legt danach den Baustein in die Zwischenablage
        var clipboard = scripts && SnippetScript.UsesClipboard(text) ? PasteService.TryGetClipboardText() : "";
        var selection = "";
        if (scripts && SnippetScript.UsesSelection(text))
        {
            if (await _paste.CopySelectionAsync(targetWindow) is not { } copied)
                return new(string.Format(Strings.TargetElevatedFormat, name, ProcessAccess.ProcessName(NativeMethods.GetForegroundWindow())),
                    MessageBoxImage.Information, gate is null ? null : 0);
            selection = copied;
        }
        IReadOnlyDictionary<string, string>? answers = null;
        if (scripts && SnippetScript.Questions(text) is { Count: > 0 } questions)
        {
            // Nach selection: das Fenster nimmt dem Zielfenster den Fokus, Einfügen holt es danach zurück
            answers = AskDialog.Ask(name, questions, _lastAnswers);
            if (answers is null)
                return gate is null ? null : new("", MessageBoxImage.Information, 0, Stopped: true);
            foreach (var (label, answer) in answers)
                _lastAnswers[label] = answer;
        }

        using var session = scripts
            ? new ScriptSession(new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, clipboard,
                Form: form, Run: request => ScriptRunner.Run(request, PasteService.EscapeDown), Selection: selection, Answers: answers,
                Folder: folder, Message: ScriptMessage))
            : null;
        if (session is not null)
        {
            try
            {
                // run() an seiner Stelle: Steht davor schon ein Tag, werden die Blöcke ab dem ersten run() erst
                // ausgewertet, wenn der Lauf dort ankommt. Sonst passiert vorher nichts, und alles läuft jetzt.
                if (form is not null && ScriptSession.NeedsStaging(text, form))
                    text = session.RenderStaged(text);
                else if (ScriptSession.Waits(text))
                    text = await Task.Run(() => session.Render(text)); // run() und msg() warten, die Oberfläche nicht
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

        string? html = null;
        var caretBack = 0;
        var planned = form is not null && KeyTags.HasMarkers(text) ? KeyTags.SplitWithSources(text, form) : null;
        if (planned is not null && PasteContent.Fits(planned.Select(s => s.Step)))
        {
            // Nur {cursor} und {html}: wie Text einfügen, mit der eingestellten Methode
            var content = PasteContent.From(planned.Select(s => s.Step));
            (text, html, caretBack, planned) = (content.Text, content.Html, content.CaretBack, null);
        }

        if (planned is not null)
        {
            // Unpassende {if}-Blöcke: gar nicht erst anfangen, statt mittendrin falsch abzubiegen
            if (KeyTags.CheckBlocks([.. planned.Select(s => s.Step)]) is { } broken)
                return new(string.Format(Strings.FormBlocksFormat, name, broken.Error), MessageBoxImage.Warning, broken.Step);

            var result = await _paste.RunFormAsync(planned, targetWindow, Settings.KeyTagsWindowGuard, gate,
                session is null ? null : ExpandAsync, TestAsync);
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

            // {if,js="…"}: jetzt auswerten, mit den Variablen der Blöcke davor
            async Task<ScriptTest> TestAsync(string expression)
            {
                var tag = "{if,js=\"" + expression + "\"}";
                if (session is null)
                    return new ScriptTest(false, string.Format(Strings.FormConditionNoScriptsFormat, name, tag));
                try
                {
                    return new ScriptTest(await Task.Run(() => session.Test(expression)));
                }
                catch (SnippetScriptException ex)
                {
                    Log.Error($"Bedingung {tag} fehlgeschlagen: {logPath ?? name}", ex);
                    return new ScriptTest(false, string.Format(Strings.FormConditionFailedFormat, name, tag, ex.Message));
                }
                catch (OperationCanceledException)
                {
                    return new ScriptTest(false, Cancelled: true);
                }
            }
        }


        // Nichts mehr übrig, z.B. ein Baustein, der nur eine Meldung zeigt: kein leeres Strg+V ins Zielfenster
        if (text.Length == 0 && html is null)
            return null;

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
        if (!await _paste.PasteAsync(text, mode, targetWindow, html, caretBack))
            return new(string.Format(Strings.TargetElevatedFormat, name, ProcessAccess.ProcessName(NativeMethods.GetForegroundWindow())),
                MessageBoxImage.Information, gate is null ? null : 0);
        return null;
    }

    /// <summary>
    /// msg() und confirm() aus einem Skript: Das läuft im Hintergrund, die Meldung gehört auf den UI-Thread. Mit after
    /// kommt sie erst später, der Baustein läuft gleich weiter. Wird QuickHasteBot vorher beendet, fällt sie weg.
    /// </summary>
    static bool? ScriptMessage(MessageRequest request)
    {
        if (request.AfterSeconds > 0)
        {
            Log.Info($"msg() in {request.AfterSeconds} s: {request.Title ?? "QuickHasteBot"}");
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(request.AfterSeconds) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ShowScriptMessage(request);
                };
                timer.Start();
            });
            return true;
        }
        return Application.Current.Dispatcher.Invoke(() => ShowScriptMessage(request));
    }

    static bool? ShowScriptMessage(MessageRequest request) =>
        MessageDialog.ShowScript(request.Text, request.Title, request.Icon switch
        {
            MessageIcon.Info => MessageBoxImage.Information,
            MessageIcon.Question => MessageBoxImage.Question,
            MessageIcon.Warning => MessageBoxImage.Warning,
            MessageIcon.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.None,
        }, request.CloseSeconds, request.Confirm);

    /// <summary>Was ask() bisher eingegeben bekam, als Vorschlag beim nächsten Mal. Nur solange das Programm läuft.</summary>
    readonly Dictionary<string, string> _lastAnswers = [];

    /// <summary>Der Baustein zu einem {include,…}, null wenn es keinen gibt.</summary>
    /// <exception cref="IncludeException">Die Datei liess sich nicht lesen.</exception>
    internal SnippetIncludes.Included? ResolveInclude(string path)
    {
        if (Library.FindIncluded(path) is not { } snippet) return null;
        try
        {
            return new(snippet.FullPath, SnippetLibrary.ReadText(snippet));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Eingebundener Baustein konnte nicht gelesen werden: {snippet.FullPath}", ex);
            throw new IncludeException(IncludeProblem.Unreadable, $"{path}: {ex.Message}");
        }
    }

    internal static string IncludeMessage(IncludeException ex) => ex.Problem switch
    {
        IncludeProblem.NotFound => string.Format(Strings.IncludeNotFoundFormat, ex.Detail),
        IncludeProblem.Cycle => string.Format(Strings.IncludeCycleFormat, ex.Detail),
        IncludeProblem.TooDeep => string.Format(Strings.IncludeTooDeepFormat, SnippetIncludes.MaxDepth, ex.Detail),
        _ => ex.Detail,
    };

    /// <summary>
    /// Beginnt der Text mit {window,…} oder {start,…}, holt er sein Fenster selbst: dann dieses Tag, sonst null. Der
    /// Editor wartet dann nicht auf den Klick ins Zielfeld.
    /// </summary>
    internal string? StartWindowTag(string text) => StartWindowTag(text, Settings.EvaluateKeyTags, Settings.EvaluateScripts, ResolveInclude);

    /// <summary>
    /// Wie <see cref="StartWindowTag(string)"/>. Skripte sehen eine leere Zwischenablage und keine Antworten auf ask(), ein
    /// Skriptfehler gibt null.
    /// </summary>
    internal static string? StartWindowTag(string text, bool keyTags, bool scripts, Func<string, SnippetIncludes.Included?>? resolve = null)
    {
        if (!keyTags) return null;
        var form = new FormBuilder();
        text = KeyTags.StripMarkers(text);
        if (resolve is not null)
        {
            try
            {
                text = SnippetIncludes.Expand(text, resolve, form);
            }
            catch (IncludeException)
            {
                return null;
            }
        }
        text = KeyTags.MarkTags(text, form);
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
    /// Was ein Bausteintext ergibt, ohne etwas zu senden. Liest die Zwischenablage nur, wenn ein Skript sie erwähnt.
    /// </summary>
    internal SnippetPreview Preview(string text) => Preview(text, 0, text.Length);

    /// <summary>
    /// Was ein Stück eines Bausteintexts ergibt, ohne etwas zu senden: für die Vorschau im Editor, der Skriptblock an der
    /// Schreibmarke oder die Auswahl. Die Blöcke davor laufen vorher still mit, damit ihre Variablen gelten. run()
    /// startet nichts und liefert ein leeres Ergebnis.
    /// </summary>
    internal SnippetPreview Preview(string text, int start, int length)
    {
        var before = KeyTags.StripMarkers(text[..start]);
        text = KeyTags.StripMarkers(text.Substring(start, length));
        var form = Settings.EvaluateKeyTags ? new FormBuilder() : null;
        if (form is not null)
        {
            // Davor zählen nur die Variablen der Blöcke: Kommentare weg, die Tasten in einen eigenen Builder
            var beforeForm = new FormBuilder();
            try
            {
                before = SnippetIncludes.Expand(before, ResolveInclude, beforeForm);
            }
            catch (IncludeException)
            {
            }
            before = KeyTags.MarkTags(before, beforeForm);
            try
            {
                text = SnippetIncludes.Expand(text, ResolveInclude, form);
            }
            catch (IncludeException ex)
            {
                return new("", false, IncludeMessage(ex));
            }
            text = KeyTags.MarkTags(text, form);
        }

        if (Settings.EvaluateScripts && SnippetScript.HasBlocks(text))
        {
            var clipboard = SnippetScript.UsesClipboard(before + text) ? PasteService.TryGetClipboardText() : "";
            try
            {
                // Ohne Run startet run() nichts
                using var session = new ScriptSession(new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, clipboard, Form: form));
                session.Prepare(before);
                text = session.Render(text);
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
        string text;
        try
        {
            text = KeyTags.MarkTags(SnippetIncludes.Expand(KeyTags.StripMarkers(tag), ResolveInclude, form), form);
        }
        catch (IncludeException ex)
        {
            return IncludeMessage(ex);
        }
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

    /// <summary>Die eigene Vorlage für "Skriptblock mit KI", ai-prompt.txt mit {task}.</summary>
    internal PromptTemplate AiTemplate => new(_paths.AiPromptFile, ScriptPrompt.DefaultTemplate);

    /// <summary>Die eigene Vorlage für "HasteScript AI", hastescript-prompt.txt.</summary>
    internal PromptTemplate HasteScriptTemplate => new(_paths.HasteScriptPromptFile, HasteScriptPrompt.DefaultTemplate);

    public string? ReadAiPromptTemplate() => ReadTemplate(AiTemplate);

    public void EditAiPromptTemplate() => EditTemplate(AiTemplate);

    public bool HasAiPromptTemplate => AiTemplate.Exists;

    public void ResetAiPromptTemplate(nint owner) => ResetTemplate(AiTemplate, owner);

    internal string CreateAiPromptTemplate() => CreateTemplate(AiTemplate);

    /// <summary>Inhalt der Vorlage, null ohne Datei (dann gilt der eingebaute Prompt).</summary>
    internal static string? ReadTemplate(PromptTemplate template)
    {
        if (!template.Exists) return null;
        try
        {
            return TextFileReader.Read(template.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht gelesen werden: {template.Path}", ex);
            return null;
        }
    }

    /// <summary>Legt die Vorlage beim ersten Mal mit dem eingebauten Prompt an und öffnet sie im Editor.</summary>
    internal void EditTemplate(PromptTemplate template)
    {
        try
        {
            CreateTemplate(template);
            ShellOpen(template.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"KI-Vorlage konnte nicht angelegt werden: {template.Path}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptTemplateErrorFormat, template.FileName, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Zurück zum eingebauten Prompt: Die Vorlage kommt in den Papierkorb, eigene Änderungen bleiben so wiederherstellbar.</summary>
    internal static void ResetTemplate(PromptTemplate template, nint owner)
    {
        if (!template.Exists) return;
        try
        {
            RecycleBin.Delete(template.Path, owner);
        }
        catch (IOException ex)
        {
            Log.Error($"KI-Vorlage konnte nicht gelöscht werden: {template.Path}", ex);
            MessageDialog.Show(string.Format(Strings.AiPromptResetErrorFormat, template.FileName, ex.Message), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <returns>Pfad der Vorlage. Eine vorhandene bleibt, wie sie ist.</returns>
    internal static string CreateTemplate(PromptTemplate template)
    {
        if (!template.Exists)
            File.WriteAllText(template.Path, template.Default);
        return template.Path;
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
        _updateTimer?.Stop();
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
    public static void OpenProjectPage() => ShellOpen(ProjectUrl);

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
