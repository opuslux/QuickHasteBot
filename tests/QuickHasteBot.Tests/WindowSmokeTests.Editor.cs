using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>Editor: Grösse und Lage, Suchen/Ersetzen, Umbruch, Schrittlauf, HasteScript AI, Start mit {window}, Tags einfügen.</summary>
public partial class WindowSmokeTests
{
    /// <summary>
    /// Der Editor merkt sich beim Schliessen Grösse, Stelle und Maximiert, der nächste geht dort auf, ein zweiter offener
    /// etwas versetzt. Läge die Stelle auf keinem Bildschirm (Monitor abgesteckt), holt Windows ihn zurück.
    /// </summary>
    static void EditorRemembersBounds()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        var folder = Path.Combine(dir.Path, "structure");
        SnippetEditorWindow Show()
        {
            var editor = SnippetEditorWindow.New(controller, folder);
            editor.ShowActivated = false;
            editor.Show();
            return editor;
        }
        nint Handle(Window window) => new System.Windows.Interop.WindowInteropHelper(window).Handle;

        // Ohne gemerkte Stelle wie bisher in der Mitte, beim Schliessen gemerkt und gespeichert
        Assert.Null(controller.Settings.EditorBounds);
        var first = Show();
        Assert.True(NativeMethods.SetWindowPos(Handle(first), 0, 140, 120, 900, 650, 0x0014)); // SWP_NOZORDER | SWP_NOACTIVATE
        var placed = WindowPlacement.Read(first)!;
        Assert.Equal((900, 650, false), (placed.Width, placed.Height, placed.Maximized));
        first.Close();
        Assert.Equal(placed, controller.Settings.EditorBounds);
        Assert.Contains(@"""EditorBounds"": {", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));

        // Der nächste geht dort auf, ein weiterer daneben versetzt; maximiert bleibt maximiert
        var second = Show();
        Assert.Equal(placed, WindowPlacement.Read(second));
        var third = Show();
        Assert.Equal(placed with { Left = placed.Left + 32, Top = placed.Top + 32 }, WindowPlacement.Read(third));
        third.Close();
        // Bei mehreren Monitoren maximiert Windows ein nie aktiviertes Fenster mitunter auf einem anderen Bildschirm
        // (dort, wo die Maus oder das Vordergrundfenster ist), und die Stelle zum Wiederherstellen wandert mit. Darum
        // zählt hier nicht der Bildschirm, sondern: maximiert gemerkt, gleiche Grösse, und der nächste stellt genau
        // die gemerkte Stelle wieder her.
        second.WindowState = WindowState.Maximized;
        second.Close();
        var saved = controller.Settings.EditorBounds!;
        Assert.Equal((placed.Width, placed.Height, true), (saved.Width, saved.Height, saved.Maximized));
        var maximized = Show();
        Assert.Equal(WindowState.Maximized, maximized.WindowState);
        maximized.WindowState = WindowState.Normal;
        Assert.Equal(saved with { Maximized = false }, WindowPlacement.Read(maximized));
        maximized.Close();

        // Weit ausserhalb jedes Bildschirms: Windows holt ihn zurück
        controller.Settings.EditorBounds = new WindowBounds(-30000, -30000, 800, 600);
        var lost = Show();
        Assert.NotEqual(0, NativeMethods.MonitorFromWindow(Handle(lost), 0)); // MONITOR_DEFAULTTONULL
        lost.Close();
    }

    /// <summary>Suchen und Ersetzen im Editor (Strg+F/Strg+H): Leiste, Zähler, Weitersuchen, Ersetzen, ein Undo, Esc.</summary>
    static void EditorFindAndReplace()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        editor.BodyInput.Text = "Hallo Welt{tab}\r\nhallo Bob{tab}\r\nHALLO";
        Snapshot(editor, "editor-find-closed.png");
        var bar = editor.FindBar;
        Assert.False(bar.IsOpen);

        void Escape() => editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });

        // Strg+F mit einzeiliger Auswahl: sie wird zum Suchtext und ist der erste Treffer, ohne Gross-/Kleinschreibung
        editor.BodyInput.Select(0, 5);
        bar.Open(replace: false);
        Assert.True(bar.IsOpen);
        Assert.False(bar.ShowsReplace);
        Assert.Equal("Hallo", bar.Query);
        Assert.Equal([0, 17, 33], bar.Matches);
        Assert.Equal((0, 5), (editor.BodyInput.SelectionStart, editor.BodyInput.SelectionLength));
        Assert.Equal(string.Format(Strings.FindCountFormat, 1, 3), bar.CountDisplay);

        // Weiter mit Umlauf, zurück
        bar.FindNext(backwards: false);
        Assert.Equal(17, editor.BodyInput.SelectionStart);
        bar.FindNext(backwards: false);
        bar.FindNext(backwards: false);
        Assert.Equal(0, editor.BodyInput.SelectionStart);
        bar.FindNext(backwards: true);
        Assert.Equal(33, editor.BodyInput.SelectionStart);
        Assert.Equal(string.Format(Strings.FindCountFormat, 3, 3), bar.CountDisplay);

        // Aa: nur noch genau so geschrieben. Kein Treffer sagt es.
        bar.MatchCase = true;
        Assert.Equal([0], bar.Matches);
        bar.Query = "Hallöchen";
        Assert.Equal(Strings.FindNoResults, bar.CountDisplay);
        bar.MatchCase = false;

        // Strg+H: Ersetzen-Zeile. Ohne ausgewählten Treffer wählt Ersetzen erst einen aus, dann ersetzt es ihn und geht weiter.
        bar.Query = "{tab}";
        editor.BodyInput.Select(0, 0);
        bar.Open(replace: true);
        Assert.True(bar.ShowsReplace);
        Snapshot(editor, "editor-find.png");
        bar.Replacement = "{enter}";
        bar.ReplaceCurrent();
        Assert.Equal((10, 5), (editor.BodyInput.SelectionStart, editor.BodyInput.SelectionLength));
        Assert.Equal("Hallo Welt{tab}\r\nhallo Bob{tab}\r\nHALLO", editor.BodyInput.Text);
        bar.ReplaceCurrent();
        Assert.Equal("Hallo Welt{enter}\r\nhallo Bob{tab}\r\nHALLO", editor.BodyInput.Text);
        Assert.Equal("{tab}", editor.BodyInput.SelectedText);

        // Alle ersetzen: ein Schritt, ein Strg+Z macht alles rückgängig, die Meldung unten sagt wie viele
        bar.Query = "hallo";
        bar.Replacement = "Hi";
        Assert.Equal(3, bar.ReplaceAll());
        Assert.Equal("Hi Welt{enter}\r\nHi Bob{tab}\r\nHi", editor.BodyInput.Text);
        Assert.Equal(string.Format(Strings.FindReplacedFormat, 3), editor.StatusText.Text);
        Assert.Equal(Strings.FindNoResults, bar.CountDisplay);
        editor.BodyInput.Undo();
        Assert.Equal("Hallo Welt{enter}\r\nhallo Bob{tab}\r\nHALLO", editor.BodyInput.Text);

        // Gesperrter Text (Schrittlauf): nichts zu ersetzen
        editor.LockForStepRun(true);
        Assert.False(bar.ReplaceAllButton.IsEnabled);
        Assert.Equal(0, bar.ReplaceAll());
        editor.LockForStepRun(false);
        Assert.True(bar.ReplaceAllButton.IsEnabled);

        // Esc schliesst zuerst die Leiste, nicht den Editor. F3 findet danach weiter mit dem letzten Suchtext.
        Escape();
        Assert.False(bar.IsOpen);
        Assert.True(editor.IsVisible);
        editor.BodyInput.Select(0, 0);
        bar.FindNext(backwards: false);
        Assert.Equal((0, 5), (editor.BodyInput.SelectionStart, editor.BodyInput.SelectionLength));

        editor.BodyInput.Text = "";
        editor.Close();
    }

    /// <summary>Umbruch-Schalter unten rechts im Editor, wie in der Textvorschau des Popups, und Shift+Mausrad.</summary>
    static void EditorWrapToggle()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        editor.BodyInput.Text = "Kurz\r\n" + new string('x', 400);
        Snapshot(editor, "editor-wrap.png");

        // Standard wie bisher: umgebrochen, der Schalter an
        Assert.True(editor.BodyInput.WordWrap);
        Assert.True(editor.WrapToggle.IsChecked);
        Assert.Equal(Strings.WrapTooltip, editor.WrapToggle.ToolTip);

        // Aus: ungebrochen und waagrecht scrollbar, auch mit Shift+Mausrad. Gilt sofort und wird gespeichert.
        editor.WrapToggle.IsChecked = false;
        editor.WrapToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.False(editor.BodyInput.WordWrap);
        Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, editor.BodyInput.HorizontalScrollBarVisibility);
        Assert.False(controller.Settings.WrapEditor);
        Assert.Contains(@"""WrapEditor"": false", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        editor.UpdateLayout();
        Assert.True(ShiftWheel.GetHorizontal(editor.BodyInput));
        Assert.True(ShiftWheel.Scroll(ShiftWheel.Viewer(editor.BodyInput)!, -120, ModifierKeys.Shift));
        editor.UpdateLayout();
        Assert.True(editor.BodyInput.HorizontalOffset > 0);

        // Ein Editor, der danach aufgeht, übernimmt die Einstellung
        var next = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        Assert.False(next.BodyInput.WordWrap);
        Assert.False(next.WrapToggle.IsChecked);
        next.Close();

        // Zeilennummern: Standard aus, der Schalter links vom Umbruch zeigt sie, Haltepunkte bleiben ganz links,
        // ohne die gepunktete Linie von AvalonEdit. Gespeichert und für den nächsten Editor.
        Assert.False(editor.BodyInput.ShowLineNumbers);
        Assert.False(editor.LineNumbersToggle.IsChecked);
        Assert.Equal(Strings.LineNumbersTooltip, editor.LineNumbersToggle.ToolTip);
        editor.LineNumbersToggle.IsChecked = true;
        editor.LineNumbersToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var margins = editor.BodyInput.TextArea.LeftMargins;
        Assert.True(editor.BodyInput.ShowLineNumbers);
        Assert.Same(editor.Breakpoints, margins[0]);
        Assert.IsType<ICSharpCode.AvalonEdit.Editing.LineNumberMargin>(margins[1]);
        Assert.DoesNotContain(margins, ICSharpCode.AvalonEdit.Editing.DottedLineMargin.IsDottedLineMargin);
        Assert.True(controller.Settings.ShowLineNumbers);
        Assert.Contains(@"""ShowLineNumbers"": true", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        editor.BodyInput.Text = string.Join("\r\n", Enumerable.Range(1, 12).Select(i => "Zeile " + i));
        Snapshot(editor, "editor-line-numbers.png");
        var numbered = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        Assert.True(numbered.BodyInput.ShowLineNumbers);
        Assert.Same(numbered.Breakpoints, numbered.BodyInput.TextArea.LeftMargins[0]);
        numbered.Close();

        // Wieder aus: nur noch die Haltepunkte
        editor.LineNumbersToggle.IsChecked = false;
        editor.LineNumbersToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.False(editor.BodyInput.ShowLineNumbers);
        Assert.Equal([editor.Breakpoints], margins.ToArray());
        Assert.False(controller.Settings.ShowLineNumbers);

        editor.BodyInput.Text = "";
        editor.Close();
    }

    /// <summary>Die Leiste des Schritt-für-Schritt-Laufs: Schritte, Ergebnis, und dass sie nie aktiv wird.</summary>
    static void StepBarShowsStepsAndResult()
    {
        UiLanguage.Apply("de");
        var form = new FormBuilder();
        var text = KeyTags.MarkTags("Freundliche Grüsse{tab}{enter}", form);
        var steps = KeyTags.SplitWithSources(text, form);

        var bar = new StepBar();
        bar.ShowStep(steps, paste: false, index: 1, paused: true);
        Snapshot(bar, "stepbar-paused.png");
        var texts = Texts(bar);
        Assert.Contains("Schritt 2 von 3", texts);
        Assert.Contains("{tab}", texts);
        Assert.Contains("{enter}", texts);
        Assert.Contains("Schritt  F10", Buttons(bar));
        Assert.Contains("Weiter  F5", Buttons(bar));

        // „Jetzt“ in der Farbe der Editor-Markierung: angehalten gelb, läuft blau
        Assert.Same(StepColors.For(StepMark.Paused).Fill, NowBox(bar).Background);
        bar.ShowNote("Feld nicht da");
        Assert.Same(bar.FindResource("SystemFillColorCautionBrush"), Text(bar, "Feld nicht da").Foreground);

        // Nie aktiv: weder beim Zeigen noch bei einem Klick
        var hwnd = new System.Windows.Interop.WindowInteropHelper(bar).Handle;
        var style = (long)Interop.NativeMethods.GetWindowLongPtr(hwnd, Interop.NativeMethods.GWL_EXSTYLE);
        Assert.NotEqual(0, style & Interop.NativeMethods.WS_EX_NOACTIVATE);
        Assert.False(bar.IsActive);

        bar.ShowStep(steps, paste: false, index: 2, paused: false);
        bar.UpdateLayout();
        Assert.Contains("(Ende)", Texts(bar));
        Assert.Same(StepColors.For(StepMark.Running).Fill, NowBox(bar).Background);

        bar.ShowResult("Fertig", null, StepResult.Done);
        Assert.Same(bar.FindResource("SystemFillColorSuccessBrush"), Text(bar, "Fertig").Foreground);
        bar.ShowResult("Gestoppt", null, StepResult.Stopped);
        Assert.Same(bar.FindResource("SystemFillColorCautionBrush"), Text(bar, "Gestoppt").Foreground);
        bar.ShowResult("Abgebrochen bei Schritt 2", "Nichts gefunden", StepResult.Failed);
        Snapshot(bar, "stepbar-failed.png");
        Assert.Same(bar.FindResource("SystemFillColorCriticalBrush"), Text(bar, "Abgebrochen bei Schritt 2").Foreground);
        Assert.Contains("Nichts gefunden", Texts(bar));
        Assert.Equal(["Nochmal", "Schliessen"], Buttons(bar));

        var choice = bar.WaitForChoiceAsync();
        FindButton(bar, "Nochmal").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(choice.IsCompleted && choice.Result);
        bar.Close();

        static List<string> Texts(Window window) => Descendants<System.Windows.Controls.TextBlock>(window)
            .Where(t => t.IsVisible).Select(t => t.Text).ToList();

        static System.Windows.Controls.TextBlock Text(Window window, string text) =>
            Descendants<System.Windows.Controls.TextBlock>(window).Single(t => t.IsVisible && t.Text == text);

        static System.Windows.Controls.Border NowBox(Window window) =>
            Descendants<System.Windows.Controls.Border>(window).Single(b => b.Child is System.Windows.Controls.TextBlock { Text: "{tab}" or "{enter}" } && b.IsVisible);

        static List<string> Buttons(Window window) => Descendants<System.Windows.Controls.Button>(window)
            .Where(b => b.IsVisible).Select(b => (string)b.Content).ToList();

        static System.Windows.Controls.Button FindButton(Window window, string text) =>
            Descendants<System.Windows.Controls.Button>(window).Single(b => b.IsVisible && (string)b.Content == text);
    }

    /// <summary>
    /// HasteScript AI: Knopf in der Leiste (passt auch bei Mindestbreite), Fenster pro Editor, der Prompt mit Skript,
    /// Problemen, Markierung und den deutschen Knopfnamen, die eigene Vorlage, die Aufgabe bleibt bis der Editor zugeht.
    /// </summary>
    static void EditorHasteScriptAi()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        editor.NameInput.Text = "Anmeldung";
        const string text = "Hallo{tab,x}\r\nZeile 2\r\n{{ 1 + }}";
        editor.BodyInput.Text = text;
        Snapshot(editor, "editor-hastescript-ai-button.png");

        // Kein Knopf der Leiste wird abgeschnitten, auch nicht beim schmalsten Editor mit den längeren deutschen Namen:
        // Was nicht passt, bricht in eine zweite Zeile um, links von der Hilfe
        Assert.Equal("JavaScript-KI", Strings.EditorAi);
        // Ein neuer Editor ist breit genug für eine Zeile
        Assert.Equal(editor.TagsButton.TranslatePoint(default, editor).Y, editor.RecordButton.TranslatePoint(default, editor).Y);
        foreach (var width in new[] { editor.Width, editor.MinWidth })
        {
            editor.Width = width;
            editor.UpdateLayout();
            var buttons = editor.ToolbarButtons;
            foreach (var button in buttons.Children.Cast<FrameworkElement>())
            {
                button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var corner = button.TranslatePoint(new Point(button.DesiredSize.Width - button.Margin.Right, button.ActualHeight), buttons);
                Assert.True(corner.X <= buttons.ActualWidth + 0.5 && corner.Y <= buttons.ActualHeight + 0.5,
                    $"Bei Breite {width} abgeschnitten: {button.ToolTip} endet bei {corner}, Platz {buttons.ActualWidth}×{buttons.ActualHeight}");
            }
            Assert.True(editor.HelpButton.TranslatePoint(new Point(0, 0), editor).X >= buttons.TranslatePoint(new Point(buttons.ActualWidth, 0), editor).X);
        }
        Snapshot(editor, "editor-toolbar-narrow.png");

        editor.BodyInput.Select(text.IndexOf("Zeile", StringComparison.Ordinal), "Zeile 2".Length);
        var ai = editor.ShowHasteScriptAi();
        Assert.Same(ai, editor.ShowHasteScriptAi());
        Assert.Same(editor, ai.Owner);
        Snapshot(ai, "hastescript-ai.png");
        Assert.Equal("Anmeldung – HasteScript-KI", ai.Title);
        Assert.Equal("Kommt mit: das Skript (3 Zeilen), die Markierung (Zeile 2), Probleme laut Editor (2), die HasteScript-Referenz und deine Aufgabe.",
            ai.IncludesText.Text);
        Assert.Equal(Visibility.Visible, ai.Placeholder.Visibility);
        Assert.Equal(Visibility.Collapsed, ai.ResetTemplateLink.Visibility);

        // Leer: auf Fehler prüfen. Das rote Tag und der Skriptfehler kommen mit, die Knöpfe heissen wie in der Oberfläche.
        Assert.EndsWith(HasteScriptPrompt.DefaultTask + "\n", ai.Prompt);
        Assert.Contains("Name: Anmeldung.txt", ai.Prompt);
        Assert.Contains("- Line 1: {tab,x} – ", ai.Prompt);
        Assert.Contains("- Script: Block 1: ", ai.Prompt);
        Assert.Contains("I selected this part (line 2)", ai.Prompt);
        Assert.Contains("\"Element erfassen\" (Pick element)", ai.Prompt);
        Assert.Equal(ai.Prompt, ai.PromptPreview.Text);
        Assert.Contains("Zeichen", ai.SizeText.Text);

        ai.TaskInput.Text = "Auch die Telefonnummer";
        Assert.Equal(Visibility.Collapsed, ai.Placeholder.Visibility);
        Assert.EndsWith("Auch die Telefonnummer\n", ai.Prompt);

        // Eine Änderung im Editor zählt beim nächsten Aktivieren bzw. Kopieren
        editor.BodyInput.Text = "Neu{enter}";
        ai.Refresh();
        Assert.Contains("```\nNeu{enter}\n```", ai.Prompt);
        Assert.DoesNotContain("Problems found by the editor", ai.Prompt);

        // Eigene Vorlage, Zurücksetzen erst dann sichtbar
        var template = PromptTemplates.Create(controller.HasteScriptTemplate);
        Assert.Equal(Path.Combine(dir.Path, "hastescript-prompt.txt"), template);
        Assert.Equal(HasteScriptPrompt.DefaultTemplate, File.ReadAllText(template));
        File.WriteAllText(template, "Bitte: {task}\n{snippet}");
        ai.Refresh();
        Assert.Equal(Visibility.Visible, ai.ResetTemplateLink.Visibility);
        Assert.StartsWith("Bitte: Auch die Telefonnummer\n", ai.Prompt);
        Assert.EndsWith("# HasteScript reference\n\n" + HasteScriptPrompt.Docs(HelpWindow.ReadReadme()) + "\n", ai.Prompt);
        File.Delete(template);

        // Esc schliesst, die Aufgabe ist beim nächsten Öffnen wieder da
        ai.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(ai)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.False(ai.IsVisible);
        var again = editor.ShowHasteScriptAi();
        Assert.NotSame(ai, again);
        Assert.Equal("Auch die Telefonnummer", again.TaskInput.Text);

        // Geht mit dem Editor zu. Nichts geändert, was gespeichert werden müsste: ohne Rückfrage
        editor.NameInput.Text = "";
        editor.BodyInput.Text = "";
        editor.Close();
        Assert.False(again.IsVisible);
    }

    /// <summary>Editor: Knopf, Haltepunkte im Rand (Klick und F9), Markierung des Schritts, Sperre während des Laufs.</summary>
    static void EditorStepByStep()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        const string text = "Grüsse{tab}\r\nMax{enter}\r\n{{ 'MfG' + key('tab') }}";
        editor.BodyInput.Text = text;
        Snapshot(editor, "editor-step.png");

        Assert.Equal("Schritt für Schritt", editor.StepButton.Content);
        Assert.Contains(editor.Breakpoints, editor.BodyInput.TextArea.LeftMargins);

        // Klick in den Rand auf Höhe der zweiten Zeile, F9 an der Schreibmarke in der dritten
        var document = editor.BodyInput.Document;
        var view = editor.BodyInput.TextArea.TextView;
        view.EnsureVisualLines();
        var secondLine = view.GetVisualLine(2)!;
        var y = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextMiddle) - view.VerticalOffset;
        Assert.Equal(2, editor.Breakpoints.LineAt(y)?.LineNumber);
        editor.Breakpoints.Toggle(editor.Breakpoints.LineAt(y)!);
        editor.BodyInput.CaretOffset = document.GetLineByNumber(3).Offset;
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, 0, Key.F9) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal([2, 3], editor.Breakpoints.Lines);
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, 0, Key.F9) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal([2], editor.Breakpoints.Lines);

        // Welcher Schritt welchen Haltepunkt trifft
        var form = new FormBuilder();
        var steps = KeyTags.SplitWithSources(SnippetScript.Render(KeyTags.MarkTags(text, form), SnippetScriptTests.Context with { Form = form }), form);
        // Der Umbruch nach {enter} ist nur Layout, "MfG" steht ganz in Zeile 3
        Assert.Equal([null, null, 2, 2, null, null], steps.Select(s => editor.BreakpointLine(text, s.Source)));

        // Haltepunkte wandern mit dem Text
        editor.BodyInput.Document.Insert(0, "Neu\r\n");
        Assert.Equal([3], editor.Breakpoints.Lines);
        editor.BodyInput.Document.Remove(0, 5);
        Assert.Equal([2], editor.Breakpoints.Lines);
        Assert.Equal(text, editor.BodyInput.Text);

        // Der Schritt wird markiert, während des Laufs ist der Text gesperrt
        editor.LockForStepRun(true);
        Assert.True(editor.BodyInput.IsReadOnly);
        Assert.False(editor.TestButton.IsEnabled);
        Assert.Equal("Lauf beenden", editor.StepButton.Content);
        editor.ShowStep(text, new StepView(3, steps[3].Source, StepMark.Paused));
        Assert.Equal(StepMark.Paused, editor.StepMarker.Mark);
        Assert.Equal("{enter}", text.Substring(editor.StepMarker.Segment!.StartOffset, editor.StepMarker.Segment.Length));
        Snapshot(editor, "editor-step-paused.png");
        editor.ShowStep(text, new StepView(5, steps[5].Source, StepMark.Failed));
        Assert.Equal("{{ 'MfG' + key('tab') }}", text.Substring(editor.StepMarker.Segment!.StartOffset, editor.StepMarker.Segment.Length));
        editor.LockForStepRun(false);
        Assert.False(editor.BodyInput.IsReadOnly);
        Assert.True(editor.TestButton.IsEnabled);
        Assert.Equal("Schritt für Schritt", editor.StepButton.Content);

        // Rot bleibt bis zur nächsten Änderung
        Assert.Equal(StepMark.Failed, editor.StepMarker.Mark);
        editor.BodyInput.Document.Insert(0, "x");
        Assert.Equal(StepMark.None, editor.StepMarker.Mark);

        // Nichts geändert, was gespeichert werden müsste: ohne Rückfrage zu
        editor.BodyInput.Text = "";
        editor.Close();
    }

    /// <summary>Beginnt der Text mit {window,…}, starten „Testen“ und „Schritt für Schritt“ ohne Klick ins Zielfeld.</summary>
    static void EditorStartsWithWindow()
    {
        // Wie in der App: nach await weiter auf dem UI-Thread
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            EditorStartsWithWindowRun();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    static void EditorStartsWithWindowRun()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        // Ein Programm, das es nicht gibt: es wird nie etwas getippt
        editor.BodyInput.Text = "{window,process=qhb-gibt-es-nicht}Grüsse";
        Snapshot(editor, "editor-starts-with-window.png");
        var click = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);

        // Schritt für Schritt hält gleich vor {window} an, ohne Banner „Ins Zielfeld klicken“
        editor.StepButton.RaiseEvent(click);
        Assert.Equal(Visibility.Collapsed, editor.TestBanner.Visibility);
        Assert.StartsWith("Beginnt mit {window,process=qhb-gibt-es-nicht}", editor.StatusText.Text);
        Assert.True(PumpUntil(() => editor.StepMarker.Mark == StepMark.Paused, TimeSpan.FromSeconds(5)));
        Assert.Equal("{window,process=qhb-gibt-es-nicht}", editor.BodyInput.Text.Substring(editor.StepMarker.Segment!.StartOffset, editor.StepMarker.Segment.Length));
        editor.StepButton.RaiseEvent(click); // Lauf beenden
        Assert.True(PumpUntil(() => (string)editor.StepButton.Content == "Schritt für Schritt", TimeSpan.FromSeconds(5)));

        // Testen läuft sofort, scheitert hier am fehlenden Fenster
        editor.TestButton.RaiseEvent(click);
        Assert.Equal(Visibility.Collapsed, editor.TestBanner.Visibility);
        Assert.StartsWith("Beginnt mit {window,process=qhb-gibt-es-nicht}", editor.StatusText.Text);
        Assert.True(PumpUntil(() => !editor.StatusText.Text.StartsWith("Beginnt mit"), TimeSpan.FromSeconds(15)));
        Assert.Equal(Visibility.Collapsed, editor.TestBanner.Visibility);

        // Ohne {window} am Anfang wie bisher: erst ins Zielfeld klicken
        editor.BodyInput.Text = "Grüsse{window,process=qhb-gibt-es-nicht}";
        editor.StepButton.RaiseEvent(click);
        Assert.Equal(Visibility.Visible, editor.TestBanner.Visibility);
        editor.StepButton.RaiseEvent(click); // Warten abbrechen
        Assert.True(PumpUntil(() => editor.TestBanner.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(5)));

        editor.BodyInput.Text = "";
        editor.Close();
    }

    /// <summary>„In Editor einfügen“ aus dem Picker: jeder Tag auf eine eigene Zeile, ohne Leerzeilen.</summary>
    static void EditorInsertsPickedTagsOnTheirOwnLine()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        Snapshot(editor, "editor-insert-tag.png");
        var body = editor.BodyInput;

        // Leerer Editor, dann am Ende: jeder Tag eine Zeile, die Schreibmarke danach auf der neuen leeren Zeile
        editor.InsertTag("{window,process=chrome}");
        editor.InsertTag("{set,name=Personen,\"5\"}");
        Assert.Equal("{window,process=chrome}\r\n{set,name=Personen,\"5\"}\r\n", body.Text);
        Assert.Equal(body.Text.Length, body.CaretOffset);

        // Am Ende einer Zeile mitten im Text: Umbruch davor, keine Leerzeile danach
        body.CaretOffset = "{window,process=chrome}".Length;
        editor.InsertTag("{click,name=Zeit}");
        Assert.Equal("{window,process=chrome}\r\n{click,name=Zeit}\r\n{set,name=Personen,\"5\"}\r\n", body.Text);

        // Am Anfang einer Zeile: Umbruch danach
        body.CaretOffset = 0;
        editor.InsertTag("{delay,100}");
        Assert.StartsWith("{delay,100}\r\n{window,process=chrome}\r\n", body.Text);

        // Mitten im Text: ohne Umbruch
        body.Text = "MaxMuster";
        body.CaretOffset = 3;
        editor.InsertTag("{tab}");
        Assert.Equal("Max{tab}Muster", body.Text);

        // Die Umbrüche sind nur Layout: gesendet werden nur die Tags
        body.Text = "";
        editor.InsertTag("{window,process=chrome}");
        editor.InsertTag("{click,name=Zeit}");
        Assert.Equal("{window,process=chrome}{click,name=Zeit}", controller.Runner.Preview(body.Text).Text);

        // In einem eingerückten Zweig: die Tags übernehmen die Einrückung, auch die Schreibmarke danach
        body.Text = "{if,process=x}\r\n    ";
        body.CaretOffset = body.Text.Length;
        editor.InsertTag("{tab}");
        editor.InsertTag("{enter}");
        Assert.Equal("{if,process=x}\r\n    {tab}\r\n    {enter}\r\n    ", body.Text);
        // Steht die Schreibmarke am Zeilenanfang, beginnt der Tag hinter der Einrückung
        body.CaretOffset = "{if,process=x}\r\n".Length;
        editor.InsertTag("{esc}");
        Assert.StartsWith("{if,process=x}\r\n    {esc}\r\n    {tab}\r\n", body.Text);

        // Aufnahme in einen eingerückten Zweig: jede Zeile eingerückt
        body.Text = "{if,process=x}\r\n    ";
        body.CaretOffset = body.Text.Length;
        editor.InsertRecorded("{focus,#ort}Bern\n{click,name=OK}\n");
        Assert.Equal("{if,process=x}\r\n    {focus,#ort}Bern\r\n    {click,name=OK}\r\n    ", body.Text);

        // Strg+Umschalt+F ordnet die Einrückung, Strg+Z macht es in einem Schritt rückgängig
        body.Text = "{if,process=x}\r\n{tab}\r\n  Hallo\r\n{/if}";
        body.CaretOffset = 0;
        editor.TidyIndentation();
        Assert.Equal("{if,process=x}\r\n    {tab}\r\n    Hallo\r\n{/if}", body.Text);
        body.Undo();
        Assert.Equal("{if,process=x}\r\n{tab}\r\n  Hallo\r\n{/if}", body.Text);

        // Das Rechtsklick-Menü: oben die Symbole, darunter die Befehle; mit Auswahl heisst das Ordnen anders
        var menu = editor.EditMenu();
        var icons = Assert.IsType<System.Windows.Controls.DockPanel>(Assert.IsType<System.Windows.Controls.MenuItem>(menu.Items[0]).Header);
        Assert.Equal(5, icons.Children.OfType<System.Windows.Controls.StackPanel>().Sum(p => p.Children.Count));
        Assert.NotEqual(editor.BodyInput.FontFamily, menu.FontFamily); // nicht die Monospace-Schrift des Texts
        menu.PlacementTarget = editor.BodyInput;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        SnapshotElement(menu, "editor-context-menu.png");
        menu.IsOpen = false;
        var headers = menu.Items.OfType<System.Windows.Controls.MenuItem>().Skip(1).Select(i => i.Header as string).ToList();
        Assert.Contains(Strings.EditorMenuIndent, headers);
        Assert.Contains(Strings.EditorMenuReplace, headers);
        body.Select(0, 5);
        Assert.Contains(Strings.EditorMenuIndentSelection, editor.EditMenu().Items.OfType<System.Windows.Controls.MenuItem>().Select(i => i.Header as string));

        body.Text = "";
        editor.Close();
    }
}
