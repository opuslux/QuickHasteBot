using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>Testlauf, Schritt für Schritt und Aufnahme: die Knöpfe des Editors, die einen Baustein laufen lassen oder aufnehmen.</summary>
internal partial class SnippetEditorWindow
{
    // --- Testlauf ---

    /// <summary>
    /// Wartet, bis ein anderes Programm vorne ist (man klickt ins Zielfeld), dann läuft der Baustein dort wie beim
    /// Einfügen, mit dem Text, wie er gerade im Editor steht. Nochmals drücken bricht das Warten ab. Beginnt der Text
    /// mit {window,…} oder {start,…}, läuft er sofort, er holt sein Fenster selbst.
    /// </summary>
    async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }
        if (_stepRun is not null || _recordBar is not null) return;

        // Was läuft, wird festgehalten, bevor man wegklickt. Ein einzelnes Tag wird markiert, damit man es sieht.
        var (start, length, scope) = TestRange();
        var text = BodyInput.Text.Substring(start, length);
        if (scope is TestScope.Tag or TestScope.Script)
            BodyInput.Select(start, length);

        using var test = _test = new CancellationTokenSource(TargetTimeout);
        TestButton.Content = Strings.EditorTestCancel;
        var startTag = _app.StartWindowTag(text);
        if (startTag is null)
            ShowWaiting(scope == TestScope.All ? null : Shorten(text));
        try
        {
            var target = startTag is null ? await WaitForTargetAsync(test.Token) : Handle;
            HideWaiting();
            if (target == 0)
            {
                ShowStatus(Strings.EditorTestCancelled);
                return;
            }
            ShowStatus(Running(startTag, Strings.EditorTestRunning), StatusKind.Busy);
            var problem = await _app.RunTextAsync(text, DisplayName, alternate: false, target, folder: _folder);
            ShowStatus(problem?.Message ?? Strings.EditorTestDone, problem is null ? StatusKind.Success : StatusKind.Error);
        }
        finally
        {
            HideWaiting();
            _test = null;
            UpdateTestButton();
        }
    }

    /// <summary>
    /// Schritt für Schritt: wie „Testen“ erst ins Zielfeld klicken (ausser der Text beginnt mit {window,…} oder
    /// {start,…}), dann läuft der ganze Baustein dort, angehalten vor jedem Schritt. Die Leiste am Zielfenster und
    /// F10/F5 steuern, der Editor markiert den Schritt. Der Text ist solange gesperrt. Nochmals drücken beendet den Lauf.
    /// </summary>
    async void OnStepRun(object sender, RoutedEventArgs e)
    {
        if (_stepRun is not null)
        {
            _stepRun.Cancel();
            return;
        }
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }

        var text = BodyInput.Text;
        var startTag = _app.StartWindowTag(text);
        var target = Handle;
        if (startTag is null)
        {
            using var test = _test = new CancellationTokenSource(TargetTimeout);
            StepButton.Content = Strings.EditorTestCancel;
            ShowWaiting(null);
            try
            {
                target = await WaitForTargetAsync(test.Token);
            }
            finally
            {
                HideWaiting();
                _test = null;
                StepButton.Content = Strings.EditorStepRun;
                UpdateTestButton();
            }
        }
        if (target == 0)
        {
            ShowStatus(Strings.EditorTestCancelled);
            return;
        }

        var run = _stepRun = new StepRun(_app, text, DisplayName, target, span => BreakpointLine(text, span), _folder);
        run.Changed += (_, view) => ShowStep(text, view);
        StepMarker.Clear();
        LockForStepRun(true);
        ShowStatus(Running(startTag, Strings.EditorStepRunning), StatusKind.Busy);
        try
        {
            await run.RunAsync();
            ShowStatus(StepMarker.Mark == StepMark.Failed ? Strings.EditorStepRunFailed : Strings.EditorStepRunEnded,
                StepMarker.Mark == StepMark.Failed ? StatusKind.Error : StatusKind.Success);
        }
        finally
        {
            _stepRun = null;
            LockForStepRun(false);
            // Rot bleibt stehen, bis man den Text ändert
            if (StepMarker.Mark != StepMark.Failed)
                StepMarker.Clear();
        }
    }

    /// <summary>Der Editor selbst: Zielfenster, wenn der Text sein Fenster mit {window,…} selbst holt.</summary>
    nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Statuszeile während eines Laufs, mit dem Grund, falls er ohne Klick ins Zielfeld begann.</summary>
    static string Running(string? startTag, string running) =>
        startTag is null ? running : string.Format(Strings.EditorStartsWithWindowFormat, startTag) + " " + running;

    /// <summary>Während des Laufs: Text gesperrt, „Testen“ aus, der Knopf beendet den Lauf.</summary>
    internal void LockForStepRun(bool running)
    {
        BodyInput.IsReadOnly = running;
        TestButton.IsEnabled = !running;
        RecordButton.IsEnabled = !running;
        StepButton.Content = running ? Strings.EditorStepRunStop : Strings.EditorStepRun;
    }

    // --- Aufnahme ---

    /// <summary>
    /// Aufnehmen: wie „Testen“ erst ins Zielfenster klicken, dann schaut der Recorder zu, während man das Formular selbst
    /// ausfüllt. Die Leiste am Zielfenster zeigt den Stand, „Fertig“ setzt die Tags an die Schreibmarke. Der Text ist
    /// solange gesperrt. Nochmals drücken (oder Esc im Editor) ist wie „Fertig“.
    /// </summary>
    async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_recordBar is not null)
        {
            _recordBar.End(keep: true);
            return;
        }
        if (_test is not null)
        {
            _test.Cancel();
            return;
        }
        if (_stepRun is not null) return;

        nint target;
        using (var wait = _test = new CancellationTokenSource(TargetTimeout))
        {
            RecordButtonText.Text = Strings.EditorTestCancel;
            ShowWaiting(null, record: true);
            try
            {
                target = await WaitForTargetAsync(wait.Token);
            }
            finally
            {
                HideWaiting();
                _test = null;
                RecordButtonText.Text = Strings.EditorRecord;
            }
        }
        if (target == 0)
        {
            ShowStatus(Strings.EditorRecordCancelled);
            return;
        }

        var bar = _recordBar = new RecordBar(_app.Settings.RecordMode);
        using var recorder = new ActionRecorder();
        void Update() => bar.Show(Recording.Lines(recorder.Snapshot(), bar.Mode), recorder.Note);
        recorder.Changed += (_, _) => Dispatcher.BeginInvoke(Update);
        recorder.ForegroundChanged += (_, window) => Dispatcher.BeginInvoke(() => bar.Place(window));
        bar.ModeChanged += (_, _) =>
        {
            _app.Settings.RecordMode = bar.Mode;
            _app.SaveSettings();
            Update();
        };

        LockForRecording(true);
        ShowStatus(Strings.EditorRecording, StatusKind.Busy);
        Log.Info($"Aufnahme: {DisplayName}");
        try
        {
            recorder.Start();
            bar.ShowAt(target);
            var keep = await bar.WaitForEndAsync();
            var actions = await recorder.StopAsync();
            var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(BodyInput.Document, 1);
            var text = Recording.Build(actions, bar.Mode, newLine);
            if (!keep)
                ShowStatus(Strings.EditorRecordDiscarded);
            else if (text.Length == 0)
                ShowStatus(Strings.EditorRecordedNothing);
            else
            {
                LockForRecording(false);
                InsertRecorded(text);
                ShowStatus(string.Format(Strings.EditorRecordedFormat, Recording.Lines(actions, bar.Mode).Count), StatusKind.Success);
            }
        }
        finally
        {
            _recordBar = null;
            bar.Close();
            LockForRecording(false);
        }
        if (IsVisible) Activate(); // nicht, wenn der Editor während der Aufnahme geschlossen wurde
    }

    /// <summary>Während der Aufnahme: Text gesperrt, Testen aus, der Knopf beendet die Aufnahme.</summary>
    internal void LockForRecording(bool recording)
    {
        BodyInput.IsReadOnly = recording;
        TestButton.IsEnabled = StepButton.IsEnabled = !recording;
        RecordButtonText.Text = recording ? Strings.EditorRecordStop : Strings.EditorRecord;
    }

    /// <summary>
    /// Die aufgenommenen Zeilen an die Schreibmarke (ersetzt eine Markierung), auf eigenen Zeilen: Umbruch davor, wenn
    /// die Zeile schon etwas enthält. Der Text endet mit einem Umbruch, was danach steht, rückt auf die nächste Zeile.
    /// </summary>
    internal void InsertRecorded(string text)
    {
        var document = BodyInput.Document;
        var indent = LineIndent(); // in einem eingerückten Zweig: jede aufgenommene Zeile ebenso
        var start = BodyInput.SelectionStart;
        var line = document.GetLineByOffset(start);
        var before = !string.IsNullOrWhiteSpace(document.GetText(line.Offset, start - line.Offset));
        var newLine = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        Insert((before ? newLine + indent : "") + text.ReplaceLineEndings(newLine + indent));
    }

    /// <summary>Den Schritt im Text markieren und hinscrollen. <paramref name="text"/>: der Text, mit dem der Lauf begann.</summary>
    internal void ShowStep(string text, StepView view)
    {
        if (view.Mark == StepMark.None)
        {
            StepMarker.Clear();
            return;
        }
        var length = BodyInput.Document.TextLength;
        var start = Math.Min(KeyTags.UnstrippedOffset(text, view.Source.Start), length);
        var end = Math.Min(KeyTags.UnstrippedOffset(text, view.Source.End), length);
        StepMarker.Show(start, end - start, view.Mark);
        var location = BodyInput.Document.GetLocation(start);
        BodyInput.ScrollTo(location.Line, location.Column);
    }

    /// <summary>Die erste Zeile mit Haltepunkt, die der Schritt an <paramref name="span"/> berührt, sonst null.</summary>
    internal int? BreakpointLine(string text, SourceSpan span)
    {
        var document = BodyInput.Document;
        var start = Math.Min(KeyTags.UnstrippedOffset(text, span.Start), document.TextLength);
        var end = Math.Min(KeyTags.UnstrippedOffset(text, Math.Max(span.Start, span.End - 1)), document.TextLength);
        for (var line = document.GetLineByOffset(start).LineNumber; line <= document.GetLineByOffset(end).LineNumber; line++)
        {
            if (Breakpoints.Has(line))
                return line;
        }
        return null;
    }

    /// <summary>
    /// Wartet auf den Klick ins Zielfeld: Banner in der Akzentfarbe statt der Statuszeile, und ein Kärtchen am
    /// Mauszeiger, weil man beim Hinüberfahren dorthin schaut. <paramref name="part"/>: was läuft, null für alles.
    /// <paramref name="record"/>: für die Aufnahme statt für einen Lauf.
    /// </summary>
    void ShowWaiting(string? part, bool record = false)
    {
        var inlines = TestBannerText.Inlines;
        inlines.Clear();
        inlines.Add(new Run(record ? Strings.EditorRecordClickTarget : Strings.EditorTestClickTarget) { FontWeight = FontWeights.SemiBold, FontSize = 13 });
        inlines.Add(new LineBreak());
        if (record)
            inlines.Add(new Run(Strings.EditorRecordWaiting));
        else if (part is null)
            inlines.Add(new Run(string.Format(Strings.EditorTestRunsAllFormat, DisplayName)));
        else
        {
            inlines.Add(new Run(Strings.EditorTestRunsPart));
            inlines.Add(new Run(" " + part + " ")
            {
                FontFamily = BodyInput.FontFamily,
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            });
            inlines.Add(new Run(" "));
        }
        inlines.Add(new Run(Strings.EditorTestStops) { FontStyle = FontStyles.Italic });
        StatusBox.Visibility = Visibility.Collapsed;
        TestBanner.Visibility = Visibility.Visible;

        _hint?.Close();
        _hint = record
            ? new CursorHint(Strings.CursorHintRecordTitle, null, Strings.CursorHintRecord)
            : new CursorHint(Strings.CursorHintTitle, part,
                part is null ? string.Format(Strings.CursorHintAllFormat, DisplayName) : Strings.CursorHintEsc);
        _hint.ShowAtCursor();
    }

    void HideWaiting()
    {
        _hint?.Close();
        _hint = null;
        TestBanner.Visibility = Visibility.Collapsed;
        StatusBox.Visibility = StatusText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Für die Statuszeile: eine Zeile, höchstens 60 Zeichen.</summary>
    static string Shorten(string text)
    {
        var line = text.ReplaceLineEndings("⏎");
        return line.Length <= 60 ? line : line[..59] + "…";
    }

    /// <summary>Das Fenster, das als Nächstes vorne ist und nicht zu QuickHasteBot gehört, sobald die Maustaste los ist.</summary>
    static async Task<nint> WaitForTargetAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var pid);
            if (window != 0 && pid != Environment.ProcessId)
            {
                // Erst wenn der Klick vorbei ist, sonst landet der Text mitten in einer Markierung per Maus
                while ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 && !cancel.IsCancellationRequested)
                    await Task.Delay(30);
                await Task.Delay(150);
                return cancel.IsCancellationRequested ? 0 : window;
            }
            try
            {
                await Task.Delay(50, cancel);
            }
            catch (OperationCanceledException)
            {
            }
        }
        return 0;
    }
}
