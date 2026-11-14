using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Lässt Bausteintexte laufen: einfügen oder als Formular tippen, mit Skripten, {include}, ask(), msg() und {if}. Dazu die
/// Vorschau im Editor und der Test im Element-Picker. Die Bibliothek kommt jedes Mal frisch, sie wird beim Wechsel des
/// structure-Ordners ersetzt.
/// </summary>
internal sealed class SnippetRunner(AppSettings settings, Func<SnippetLibrary> library)
{
    readonly PasteService _paste = new();

    /// <summary>Was ask() bisher eingegeben bekam, als Vorschlag beim nächsten Mal. Nur solange das Programm läuft.</summary>
    readonly Dictionary<string, string> _lastAnswers = [];

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
        var form = settings.EvaluateKeyTags ? new FormBuilder() : null;
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
        var scripts = settings.EvaluateScripts && (SnippetScript.HasBlocks(text) || conditions);
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
            answers = AskDialog.Ask(name, questions, SnippetScript.AskWindow(text), _lastAnswers);
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

            var result = await _paste.RunFormAsync(planned, targetWindow, settings.KeyTagsWindowGuard, gate,
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

        var mode = alternate ? AppSettings.Other(settings.PasteMode) : settings.PasteMode;
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

    /// <summary>Der Baustein zu einem {include,…}, null wenn es keinen gibt.</summary>
    /// <exception cref="IncludeException">Die Datei liess sich nicht lesen.</exception>
    internal SnippetIncludes.Included? ResolveInclude(string path)
    {
        if (library().FindIncluded(path) is not { } snippet) return null;
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
    internal string? StartWindowTag(string text) => StartWindowTag(text, settings.EvaluateKeyTags, settings.EvaluateScripts, ResolveInclude);

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
        var form = settings.EvaluateKeyTags ? new FormBuilder() : null;
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

        if (settings.EvaluateScripts && SnippetScript.HasBlocks(text))
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
            if (settings.EvaluateScripts && SnippetScript.HasBlocks(text))
                text = SnippetScript.Render(text, new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, "", Form: form));
        }
        catch (SnippetScriptException ex)
        {
            return string.Format(Strings.ScriptErrorFormat, ex.Block, Strings.PickerTestName, ex.Message);
        }
        if (!KeyTags.HasMarkers(text))
            return Strings.PickerTestNoTag;

        var result = await _paste.RunFormAsync(KeyTags.SplitWithSources(text, form), targetWindow, settings.KeyTagsWindowGuard);
        return result.Outcome switch
        {
            FormOutcome.Completed => null,
            FormOutcome.EscapePressed => Strings.PickerTestStopped,
            _ => FormMessage(result, Strings.PickerTestName),
        };
    }
}
