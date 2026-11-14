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

/// <summary>
/// Lädt die Fenster offscreen mit Fluent-Theme: findet kaputtes XAML, Bindings und fehlende Theme-Ressourcen.
/// Mit QHB_SNAPSHOTS=&lt;Ordner&gt; werden die Fenster zusätzlich als PNG gespeichert.
/// Läuft allein, nicht parallel zu anderen Testklassen: echte Fenster, UI Automation und Mausklicks, dazu die Sprache
/// der Oberfläche, die für den ganzen Prozess gilt.
/// </summary>
[Collection(nameof(WindowSmokeTests))]
public class WindowSmokeTests
{
    // Eine Application pro Prozess: beide Sprachen laufen nacheinander im selben Test
    [Fact]
    public void Popup_and_settings_render_in_german_and_english() => RunOnSta(() =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
        app.Resources.Add("BoolToVisibility", new System.Windows.Controls.BooleanToVisibilityConverter());
        try
        {
            // Wie ein deutsches Windows, damit der Test überall gleich läuft. English() stellt per settings.json um.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            UiLanguage.AssumeWindowsLanguage(CultureInfo.GetCultureInfo("de"));
            German();
            English();
            SwitchLanguage();
            ThemeAndDialogs();
            FileHotkeys();
            PopupGrowsWithFolderList();
            KeptOpenPopupIsAnOrdinaryWindow();
            PopupPreviewColors();
            AskDialogAndUpdateLink();
            StepBarShowsStepsAndResult();
            EditorStepByStep();
            EditorHasteScriptAi();
            EditorStartsWithWindow();
            SetPicksFromAListElsewhere();
            ContextMenuComesFirst();
            ContainersWildcardsAndTooltips();
            FieldsBeforeTheirLabels();
            RecorderFollowsFieldsInAForm();
            RecordBarAndEditor();
            EditorInsertsPickedTagsOnTheirOwnLine();
            EditorWrapToggle();
            EditorFindAndReplace();
            EditorRemembersBounds();
            HelpShowsReadme();
            MarkdownBlocksAndClicks();
        }
        finally
        {
            app.Shutdown();
        }
    });

    /// <summary>Das README als Hilfe: Anker springen, die Überschrift steht danach oben. Auch dunkel lesbar.</summary>
    static void HelpShowsReadme()
    {
        var opened = new List<string>();
        var help = new HelpWindow(opened.Add);
        Snapshot(help, "help.png");
        Assert.Equal("QuickHasteBot – Hilfe (Englisch)", help.Title);

        // Jeder Link im Inhaltsverzeichnis und im Text findet seine Überschrift: fängt umbenannte Abschnitte im README
        var anchors = help.Markdown.Links.Where(l => l.StartsWith('#')).Select(l => l[1..]).Distinct().ToList();
        Assert.NotEmpty(anchors);
        Assert.All(anchors, a => Assert.True(help.Markdown.Anchors.ContainsKey(a), "#" + a));
        Assert.Contains("forms-key-tags", help.Markdown.Anchors.Keys);

        var scroller = help.Scroller()!;
        Assert.Equal(0, scroller.VerticalOffset);
        Assert.True(help.ScrollTo(HelpWindow.EditorAnchor));
        help.UpdateLayout();
        Assert.True(scroller.VerticalOffset > 0);
        // Die Überschrift oben im Fenster, nicht irgendwo am unteren Rand (ganz oben kann sie wegen Rundung bei -1e-12 stehen)
        var heading = (System.Windows.Documents.Paragraph)help.Markdown.Anchors[HelpWindow.EditorAnchor];
        var top = heading.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Top;
        Assert.InRange(top, -1, 120);
        Snapshot(help, "help-editor.png");
        Assert.True(help.ScrollTo("keys"));
        Snapshot(help, "help-table.png");
        Assert.False(help.ScrollTo("gibt-es-nicht"));

        // Ein Link ins Repository geht nach draussen, ein Anker nicht
        var license = help.Markdown.Document.Blocks.SelectMany(Hyperlinks).First(h => h.ToolTip as string == "LICENSE");
        license.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
        Assert.Equal(["https://github.com/opuslux/QuickHasteBot/blob/main/LICENSE"], opened);

        UiTheme.Apply(AppTheme.Dark);
        try
        {
            Snapshot(help, "help-dark.png");
        }
        finally
        {
            UiTheme.Apply(AppTheme.Light);
        }
        help.Close();
    }

    /// <summary>Markdown-Blöcke werden zu den passenden FlowDocument-Elementen, ein Klick meldet das Link-Ziel.</summary>
    static void MarkdownBlocksAndClicks()
    {
        var clicked = new List<string>();
        var markdown = MarkdownDocument.Build("""
            # Title

            Some **bold** and `code` with a [link](#second-part).

            - one
            - two

            ```
            line 1
            line 2
            ```

            | Key | Action |
            |---|---|
            | Enter | Paste |

            ## Second part
            """, clicked.Add);

        var blocks = markdown.Document.Blocks.ToList();
        Assert.IsType<System.Windows.Documents.Paragraph>(blocks[0]);
        Assert.Equal(2, Assert.IsType<System.Windows.Documents.List>(blocks[2]).ListItems.Count);
        var code = Assert.IsType<System.Windows.Documents.Paragraph>(blocks[3]);
        Assert.Equal("line 1\r\nline 2", new System.Windows.Documents.TextRange(code.ContentStart, code.ContentEnd).Text);
        var table = Assert.IsType<System.Windows.Documents.Table>(blocks[4]);
        Assert.Equal(2, table.RowGroups[0].Rows.Count);
        Assert.Equal(FontWeights.SemiBold, table.RowGroups[0].Rows[0].FontWeight);
        Assert.Same(blocks[5], markdown.Anchors["second-part"]);

        var link = ((System.Windows.Documents.Paragraph)blocks[1]).Inlines.OfType<System.Windows.Documents.Hyperlink>().Single();
        link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
        Assert.Equal(["#second-part"], clicked);
    }

    static IEnumerable<System.Windows.Documents.Hyperlink> Hyperlinks(System.Windows.Documents.TextElement element) =>
        System.Windows.LogicalTreeHelper.GetChildren(element).OfType<System.Windows.Documents.TextElement>()
            .SelectMany(child => child is System.Windows.Documents.Hyperlink link ? [link] : Hyperlinks(child));

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

    /// <summary>Blättern, Suchen und Kopfzeile, dazu die deutschen Texte.</summary>
    static void German()
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\1-Temp\00-Temp.txt", "temp");
        dir.File(@"structure\4-Grüsse\01-MfG-DE.txt", "Freundliche Grüsse\r\nMax Muster\r\nEine sehr lange Zeile, die in der Vorschau nur mit Umbruch ganz zu sehen ist, ohne muss man scrollen");
        dir.File(@"structure\4-Grüsse\03-MfG-EN.txt", "Kind regards\r\nMax Muster");
        dir.File(@"structure\4-Grüsse\Intern\01-Gruss.txt", "Gruss");
        dir.File(@"structure\6-Weblinks\www.google.com.url", "[InternetShortcut]\r\nURL=https://www.google.com");
        dir.File(@"structure\ascii-table.txt", "!\"#$%&'()*+,-./");

        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal(["00-Temp"], viewModel.DetailItems.Select(i => i.Snippet.Name));

        // Links nummeriert für Alt+1 bis Alt+9, rechts nicht
        Assert.Equal(["1", "2", "3", "4"], viewModel.Items.Select(i => i.Shortcut));
        Assert.Equal([null], viewModel.DetailItems.Select(i => i.Shortcut));
        Assert.Equal("Alt+2 – wie ein Klick auf diesen Eintrag", viewModel.Items[1].ShortcutTooltip);
        Assert.Null(viewModel.DetailItems[0].ShortcutTooltip);
        Assert.True(viewModel.SelectNumber(3));
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);
        Assert.False(viewModel.SelectNumber(5));
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

        // Rechts steht der Inhalt des markierten Ordners
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        Assert.Equal(["Intern", "01-MfG-DE", "03-MfG-EN"], viewModel.DetailItems.Select(i => i.Snippet.Name));
        Assert.True(viewModel.ShowDetailItems);
        Snapshot(popup, "popup-browse.png");

        // Ein Ordner von rechts öffnet sich direkt
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["01-Gruss"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Gruss", viewModel.DetailText);
        viewModel.GoTo("");

        // Ein Buchstabe sucht noch nicht, der Ordner bleibt stehen
        viewModel.Query = "m";
        Assert.False(viewModel.IsSearching);
        Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Bausteine", viewModel.Breadcrumbs[0].Name);

        viewModel.Query = "mfg";
        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.ShowDetailItems);
        Assert.True(viewModel.DetailIsText);
        Assert.StartsWith("Freundliche Grüsse" + Environment.NewLine + "Max Muster" + Environment.NewLine, viewModel.DetailText);
        Snapshot(popup, "popup-search.png");
        // Bei einem Text unten rechts in der Vorschau der Stift zum Bearbeiten, bei Ordnern und Weblinks nicht
        Assert.True(popup.EditPreviewButton.IsVisible);
        Assert.Equal("Diesen Baustein im Editor bearbeiten (Strg+O)", popup.EditPreviewButton.ToolTip);
        // Grau wie die übrigen Symbole: Akzentfarbe hiesse „eingeschaltet“ wie beim Umbruch
        Assert.Same(popup.FindResource("TextFillColorTertiaryBrush"), popup.EditPreviewButton.Foreground);
        // Shift+Mausrad scrollt die ungebrochene Vorschau waagrecht, ohne Shift bleibt es beim normalen Rad
        Assert.True(ShiftWheel.GetHorizontal(popup.DetailScroll));
        Assert.False(ShiftWheel.Scroll(popup.DetailScroll, -120, ModifierKeys.None));
        Assert.True(ShiftWheel.Scroll(popup.DetailScroll, -120, ModifierKeys.Shift));
        popup.UpdateLayout();
        Assert.True(popup.DetailScroll.HorizontalOffset > 0);
        popup.DetailScroll.ScrollToHorizontalOffset(0);

        // Aus der Suche heraus führt ein Ordner von rechts in den Blätter-Modus
        viewModel.Query = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        viewModel.GoTo("");

        // Beim Suchen führt der Pfeil nach rechts in den Ordnerinhalt rechts, aber nur am Ende des Suchtexts
        void Press(Key key) => popup.SearchBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(popup.SearchBox)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        popup.SearchBox.Text = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        popup.SearchBox.Select(2, 0);
        Press(Key.Right);
        Assert.False(viewModel.InDetail);
        // Ganz markiert wie nach dem Öffnen mit gemerktem Suchtext: gleich nach rechts, der Text bleibt zum Weitertippen
        popup.SearchBox.SelectAll();
        Press(Key.Right);
        Assert.Equal("Intern", viewModel.DetailSelected?.Snippet.Name);
        Assert.Equal((6, 0), (popup.SearchBox.SelectionStart, popup.SearchBox.SelectionLength));
        Press(Key.Down);
        Press(Key.Down);
        Press(Key.Down);
        Assert.Equal("03-MfG-EN", viewModel.DetailSelected?.Snippet.Name);
        Assert.Same(viewModel.DetailSelected, popup.DetailList.SelectedItem);
        Assert.Equal("03-MfG-EN", viewModel.CurrentSnippet?.Name);
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Press(Key.Up);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        // Pfeil nach links: zurück in die linke Liste, dort ist noch derselbe Ordner markiert
        Press(Key.Left);
        Assert.False(viewModel.InDetail);
        Assert.Null(popup.DetailList.SelectedItem);
        Assert.Equal("4-Grüsse", viewModel.CurrentSnippet?.Name);
        // Alt+Ziffer bzw. ein Klick links, auch auf denselben Eintrag, holt die Tastatur zurück nach links
        Press(Key.Right);
        Assert.True(viewModel.InDetail);
        viewModel.Selected = viewModel.Selected;
        Assert.False(viewModel.InDetail);
        // Ein Ordner rechts lässt die Spalten weiterrücken, aus der Suche heraus wird geblättert
        Press(Key.Right);
        Press(Key.Right);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Bausteine", "4-Grüsse"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("Intern", viewModel.Selected?.Snippet.Name);
        Assert.Equal("01-Gruss", viewModel.DetailSelected?.Snippet.Name);
        // Zurück: in die linke Liste, dann eine Ebene hoch, der Ordner von vorher bleibt markiert
        Press(Key.Left);
        Press(Key.Left);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        // Ohne Suchtext genauso: Der Pfeil nach rechts öffnet links keinen Ordner, er geht nur in die rechte Liste
        Press(Key.Right);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Assert.Equal("Intern", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Down);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Right);
        Assert.Equal("01-MfG-DE", viewModel.DetailSelected?.Snippet.Name);
        Press(Key.Left);
        Assert.False(viewModel.InDetail);
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        // Weitertippen holt die Tastatur zurück nach links
        popup.SearchBox.Text = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        popup.SearchBox.Select(popup.SearchBox.Text.Length, 0);
        Press(Key.Right);
        Assert.True(viewModel.InDetail);
        popup.SearchBox.Text = "grüss";
        Assert.False(viewModel.InDetail);
        // Bei einem Text rechts gibt es nichts auszuwählen, der Pfeil bleibt beim Suchfeld
        popup.SearchBox.Text = "mfg";
        popup.SearchBox.Select(3, 0);
        Press(Key.Right);
        Assert.False(viewModel.InDetail);

        viewModel.Query = "";
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "6-Weblinks").Snippet);
        Assert.Equal(["Bausteine", "6-Weblinks"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["", null], viewModel.Breadcrumbs.Select(c => c.Target));
        Assert.Equal("Öffnet https://www.google.com", viewModel.DetailText);
        Assert.False(viewModel.DetailIsText);
        popup.UpdateLayout();
        Assert.False(popup.EditPreviewButton.IsVisible);
        Assert.True(viewModel.GoUp());
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

        // Über die Kopfzeile zurück, der Ordner auf dem Weg bleibt ausgewählt
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal(["Bausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Snapshot(popup, "popup-breadcrumbs.png");
        viewModel.GoTo(viewModel.Breadcrumbs[0].Target!);
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);

        // Klonen: Vorschlag wie im Explorer, ungültige oder vergebene Namen sperren den Knopf
        var mfg = controller.Library.Find(@"4-Grüsse\01-MfG-DE.txt")!;
        var dialog = NameDialog.ForClone(mfg, SnippetFiles.SuggestCopyName(mfg.FullPath, Strings.CopySuffix));
        Assert.Equal("01-MfG-DE - Kopie.txt", dialog.FileName);
        Assert.True(dialog.ConfirmButton.IsEnabled);
        Snapshot(dialog, "clone.png");
        dialog.NameInput.Text = "03-MfG-EN.txt";
        Assert.Equal(Strings.NameExists, dialog.Problem.Text);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.NameInput.Text = "MfG: neu.txt";
        Assert.Equal(Strings.NameInvalid, dialog.Problem.Text);
        dialog.NameInput.Text = "  02-MfG-CH.txt ";
        Assert.Equal("", dialog.Problem.Text);
        Assert.Equal("02-MfG-CH.txt", dialog.FileName);
        dialog.Close();

        // Umbenennen: unverändert gesperrt, nur Gross/Klein zu ändern erlaubt
        var rename = NameDialog.ForRename(mfg);
        Assert.Equal("Umbenennen", rename.Title);
        Assert.Equal("01-MfG-DE.txt", rename.FileName);
        Assert.False(rename.ConfirmButton.IsEnabled);
        Assert.Equal("", rename.Problem.Text);
        Snapshot(rename, "rename.png");
        rename.NameInput.Text = "01-mfg-de.txt";
        Assert.True(rename.ConfirmButton.IsEnabled);
        rename.NameInput.Text = "03-MfG-EN.txt";
        Assert.Equal(Strings.NameExists, rename.Problem.Text);
        rename.Close();

        // Danach ist die Kopie markiert, nach dem Löschen der nächste Eintrag bzw. am Ende der vorige
        viewModel.EnterFolder(mfg.Parent!);
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "01-MfG-DE");
        var copy = SnippetFiles.Clone(mfg.FullPath, "02-MfG-CH.txt");
        controller.Library.Reload();
        viewModel.RefreshSelecting(Path.GetRelativePath(controller.Library.RootPath, copy));
        Assert.Equal(["Intern", "01-MfG-DE", "02-MfG-CH", "03-MfG-EN"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("02-MfG-CH", viewModel.Selected?.Snippet.Name);

        var next = viewModel.SelectionAfterRemoving(viewModel.Selected!.Snippet);
        File.Delete(copy);
        controller.Library.Reload();
        viewModel.Refresh(next);
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);
        Assert.Equal(@"4-Grüsse\01-MfG-DE.txt", viewModel.SelectionAfterRemoving(viewModel.Selected!.Snippet));
        // Ein umbenannter Ordner bleibt markiert
        viewModel.GoTo("");
        var renamed = SnippetFiles.Rename(controller.Library.Find("1-Temp")!.FullPath, "2-Temp");
        controller.Library.Reload();
        viewModel.RefreshSelecting(Path.GetRelativePath(controller.Library.RootPath, renamed));
        Assert.Equal("2-Temp", viewModel.Selected?.Snippet.Name);
        Assert.Equal(["00-Temp"], viewModel.DetailItems.Select(i => i.Snippet.Name));
        SnippetFiles.Rename(renamed, "1-Temp");
        controller.Library.Reload();

        // Rechts gelöscht: die Auswahl links bleibt
        viewModel.Refresh("4-Grüsse");
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);
        Assert.Equal("4-Grüsse", viewModel.SelectionAfterRemoving(mfg));

        // Mit der Stecknadel beginnt das nächste Öffnen im Ordner und beim Eintrag vom Schliessen
        viewModel.RememberFolder = true;
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "03-MfG-EN");
        // Das Popup schliesst sich selbst, sobald es nicht mehr aktiv ist. Je nach Desktop (ein anderes Fenster
        // holt sich den Fokus) ist es hier schon zu, dann würde Hide() nichts auslösen.
        popup.Show();
        popup.Hide();
        Assert.Contains(@"""LastFolder"": ""4-Grüsse""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Reset();
        Assert.Equal(["Bausteine", "4-Grüsse"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);

        // Ohne Stecknadel geht es wieder oben los, ein verschwundener Ordner ebenso
        viewModel.RememberFolder = false;
        viewModel.Reset();
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        controller.Settings.RememberFolder = true;
        controller.Settings.LastFolder = "Gibt-es-nicht";
        viewModel.Reset();
        Assert.Equal(["Bausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.True(viewModel.RememberFolder);
        controller.Settings.RememberFolder = false;

        // Mit dem Filter kommt der Suchtext samt markiertem Treffer wieder, Enter startet denselben Eintrag
        viewModel.RememberQuery = true;
        viewModel.Query = "mfg";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "03-MfG-EN");
        popup.Show();
        popup.Hide();
        Assert.Contains(@"""LastQuery"": ""mfg""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Reset();
        Assert.Equal("mfg", viewModel.Query);
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);
        // Der Hotkey eines Ordners öffnet dort ohne Suchtext
        viewModel.Reset("4-Grüsse");
        Assert.Equal("", viewModel.Query);
        // Ohne Filter beginnt es wieder leer
        viewModel.Query = "mfg";
        viewModel.RememberQuery = false;
        popup.Show();
        popup.Hide();
        viewModel.Reset();
        Assert.Equal("", viewModel.Query);

        // Umbruch der Textvorschau wird sofort gespeichert
        Assert.False(viewModel.WrapPreview);
        viewModel.WrapPreview = true;
        Assert.Contains(@"""WrapPreview"": true", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Query = "mfg";
        Snapshot(popup, "popup-wrap.png");
        viewModel.WrapPreview = false;
        viewModel.Query = "";

        // KI-Prompt: ohne eigenen Text steht ein zufälliges Beispiel da, gespeichert wird es dabei nicht
        Assert.Equal(10, AiPromptWindow.Examples.Count);
        Assert.Contains("Das Datum in 14 Tagen mit Wochentag", AiPromptWindow.Examples);
        var ai = new AiPromptWindow(controller);
        Assert.Contains(ai.RequirementInput.Text, AiPromptWindow.Examples);
        Assert.Equal("", controller.Settings.AiPromptText);
        Assert.True(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Collapsed, ai.Placeholder.Visibility);
        Assert.Equal("In ChatGPT öffnen", ai.OpenButton.Content);
        Assert.Equal("ChatGPT", ai.ServiceInput.SelectedItem?.ToString());
        Assert.Equal(Visibility.Collapsed, ai.ResetTemplateLink.Visibility);
        // Die Glühbirne geht reihum zum nächsten Beispiel, auch das wird nicht gespeichert
        var index = AiPromptWindow.Examples.ToList().IndexOf(ai.RequirementInput.Text);
        ai.NextExample();
        Assert.Equal(AiPromptWindow.Examples[(index + 1) % 10], ai.RequirementInput.Text);
        Assert.Equal("", controller.Settings.AiPromptText);
        // Ein eigener Text bleibt gespeichert, auch wenn danach ein Beispiel im Feld steht
        ai.RequirementInput.Text = "Mein eigener Text";
        ai.NextExample();
        Assert.Contains(ai.RequirementInput.Text, AiPromptWindow.Examples);
        Assert.Equal("Mein eigener Text", controller.Settings.AiPromptText);
        // Leer gelöscht: gesperrt, mit Platzhalter, die Vorschau zeigt den ganzen Prompt mit der Eingabe am Anfang
        ai.RequirementInput.Text = "";
        Assert.False(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Visible, ai.Placeholder.Visibility);
        ai.RequirementInput.Text = "Datum in 14 Tagen mit Wochentag";
        Assert.True(ai.OpenButton.IsEnabled);
        Assert.Equal(Visibility.Collapsed, ai.Placeholder.Visibility);
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen mit Wochentag", ai.PromptPreview.Text);

        // Antwort testen, leer: Platzhalter mit Beispiel, der Knopf übernimmt es, jeder weitere Klick würfelt neu
        Assert.Equal(Visibility.Collapsed, ai.TestResultBox.Visibility);
        Assert.Equal(Visibility.Visible, ai.AnswerPlaceholder.Visibility);
        Assert.Equal("Beispiel übernehmen (F5)", ai.RerunButton.ToolTip);
        Snapshot(ai, "ai-prompt-empty.png");
        string[] faces = ["Ja", "Nein", "Vielleicht", "Morgen"];
        ai.Rerun();
        Assert.StartsWith("{{ ['Ja', 'Nein'", ai.AnswerInput.Text);
        Assert.Equal(Visibility.Collapsed, ai.AnswerPlaceholder.Visibility);
        Assert.StartsWith("Erneut ausführen (F5)", (string)ai.RerunButton.ToolTip);
        Assert.Contains(ai.TestResult.Text, faces);
        var rolled = Enumerable.Range(0, 50).Select(_ => { ai.Rerun(); return ai.TestResult.Text; }).ToHashSet();
        Assert.Subset(faces.ToHashSet(), rolled);
        Assert.True(rolled.Count > 1);

        // Läuft wie beim Einfügen, Codeblock-Zäune stören nicht
        ai.AnswerInput.Text = "```js\n{{ (() => { const w = ['So', 'Mo']; return w.at(1) + ' ' + 6 * 7; })() }}\n```";
        ai.TestAnswer();
        Assert.Equal(Visibility.Visible, ai.TestResultBox.Visibility);
        Assert.Equal("Mo 42", ai.TestResult.Text);
        Assert.Equal(Visibility.Visible, ai.CopyRow.Visibility);
        Assert.Equal(Visibility.Hidden, ai.WrappedHint.Visibility);
        Assert.Equal("{{ (() => { const w = ['So', 'Mo']; return w.at(1) + ' ' + 6 * 7; })() }}", ai.Block);

        // Eine echte ChatGPT-Antwort, markiert statt mit dem Kopieren-Knopf: Sprachzeile oben, ohne {{ }}
        ai.AnswerInput.Text = """
            javascript
            const spider = [
              "        / _ \\        ",
              "      \\_\\(_)/_/      "
            ].join("\n");

            spider
            """;
        ai.TestAnswer();
        Assert.Equal("        / _ \\        \n      \\_\\(_)/_/      ".ReplaceLineEndings(), ai.TestResult.Text.ReplaceLineEndings());
        Assert.Equal(Visibility.Visible, ai.WrappedHint.Visibility);
        Assert.StartsWith("{{ const spider = [", ai.Block);
        Assert.EndsWith("spider }}", ai.Block);
        Snapshot(ai, "ai-prompt.png");

        // Editor: ANSI-Baustein öffnen, Vorschau, einfügen, speichern (bleibt ANSI), umbenennen
        var ansi = dir.Bytes(@"structure\Editor\Gruss.txt", [0x47, 0x72, 0xFC, 0x73, 0x73, 0x65]); // "Grüsse" in Windows-1252
        var editor = SnippetEditorWindow.Open(controller, ansi);
        Assert.Equal("Gruss", editor.NameInput.Text);
        Assert.Equal("Grüsse", editor.BodyInput.Text);
        Assert.Equal(".txt", editor.ExtensionText.Text);
        Assert.Equal("in Bausteine › Editor", editor.FolderText.Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("Gruss – Baustein bearbeiten", editor.Title);
        // Vorschau nur für Skripte: reiner Text steht im Editor schon so da
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);

        editor.BodyInput.Text = "Max{tab}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length, 0);
        editor.Insert("{focus,#id}", "id");
        Assert.Equal("Max{tab}{focus,#id}", editor.BodyInput.Text);
        Assert.Equal("id", editor.BodyInput.SelectedText);
        editor.BodyInput.SelectedText = "email";
        editor.BodyInput.Text += "{{ 6 * 7 }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Visible, editor.PreviewBox.Visibility);
        Assert.Equal("Dieser Block ergibt", editor.PreviewLabel.Text);
        Assert.Equal("42", editor.PreviewText.Text);
        Assert.Equal(Visibility.Collapsed, editor.PreviewNote.Visibility);
        // Die Auswahl: Tasten als lesbare Tags, das Skript ausgewertet
        editor.BodyInput.Select(0, editor.BodyInput.Text.Length);
        editor.UpdatePreview();
        Assert.Equal("Die Auswahl ergibt", editor.PreviewLabel.Text);
        Assert.Equal("Max{tab}{focus,#email}42", editor.PreviewText.Text);
        // Schreibmarke im Text, nicht im Skript: keine Vorschau
        editor.BodyInput.Select(1, 0);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.True(editor.IsDirty);
        Assert.StartsWith("● ", editor.Title);
        Snapshot(editor, "editor.png");

        // Tag-Menü: ohne Filter die Gruppen, mit Filter eine flache Trefferliste, Enter fügt den ersten Treffer ein
        var menu = editor.TagMenu();
        menu.PlacementTarget = editor.TagsButton;
        menu.IsOpen = true;
        menu.UpdateLayout();
        var filter = (System.Windows.Controls.TextBox)((System.Windows.Controls.Grid)((System.Windows.Controls.MenuItem)menu.Items[0]).Header).Children[0];
        List<string> Visible() => menu.Items.OfType<System.Windows.Controls.MenuItem>().Skip(1)
            .Where(i => i.Visibility == Visibility.Visible).Select(i => i.Header as string ?? "").ToList();
        Assert.Equal(["Tasten", "Ablauf", "Elemente und Fenster", "Skript"], Visible());
        SnapshotElement(menu, "editor-tag-menu.png");
        filter.Text = "feld";
        Assert.Equal(["{tab}", "{shift+tab}", "{clear}", "{focus,#id}", "{set,#id,\"\"}"], Visible());
        SnapshotElement(menu, "editor-tag-filter.png");
        filter.Text = "feld cursor";
        Assert.Equal(["{focus,#id}"], Visible());
        filter.Text = "gibtsnicht";
        Assert.Equal(["Kein passendes Tag"], Visible());
        filter.Text = "guid";
        editor.BodyInput.Text = "Ref ";
        editor.BodyInput.Select(4, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("Ref {{ guid() }}", editor.BodyInput.Text);
        Assert.False(menu.IsOpen);

        // Kommentar: auf eine eigene Zeile, die Notiz markiert zum Überschreiben
        // Jeder Tag hat eine Beschreibung (in der Sprache der Oberfläche, nicht der Name des Texts)
        var tags = menu.Items.OfType<System.Windows.Controls.MenuItem>()
            .Where(i => i.Header is string header && header.StartsWith('{')).ToList();
        Assert.All(tags, t => Assert.False(string.IsNullOrEmpty(t.InputGestureText) || t.InputGestureText.StartsWith("Tag"), (string)t.Header));
        Assert.Contains(tags, t => t.InputGestureText == "Pfeil hoch");

        // Der Filter findet auch die englischen Texte
        filter.Text = "comment";
        Assert.Equal(["{# Notiz}"], Visible());
        filter.Text = "arrow";
        Assert.Equal(["{up}", "{down}", "{left}", "{right}"], Visible());
        filter.Text = "keys reload";
        Assert.Equal(["{f5}"], Visible());

        // Jeder Tag erklärt sich im Tooltip. Findet der Filter sonst nichts, sucht er in diesen Hilfetexten.
        Assert.All(tags, t => Assert.False(string.IsNullOrEmpty(((System.Windows.Controls.TextBlock)((System.Windows.Controls.ToolTip)t.ToolTip).Content).Text), (string)t.Header));
        filter.Text = "arbeitsordner";
        Assert.Equal(["{{ const r = run('d:/scripts/file.bat', { args: '', dir: 'd:/data' }) }}"], Visible());
        filter.Text = "standardbrowser";
        Assert.Equal(["{start,https://example.com}"], Visible());

        filter.Text = "kommentar";
        Assert.Equal(["{# Notiz}"], Visible());
        editor.BodyInput.Text = "Hallo";
        editor.BodyInput.Select(5, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("Hallo\r\n{# Notiz}\r\n", editor.BodyInput.Text);
        Assert.Equal("Notiz", editor.BodyInput.SelectedText);
        editor.UpdatePreview();
        Assert.Equal(Visibility.Collapsed, editor.PreviewBox.Visibility);

        // run(): auf eine eigene Zeile, der Pfad markiert. Die Vorschau startet nichts, run() liefert dort ein leeres Ergebnis.
        filter.Text = "ausführen";
        Assert.Equal(["{{ const r = run('d:/scripts/file.bat') }}"], Visible());
        editor.BodyInput.Text = "{tab}";
        editor.BodyInput.Select(5, 0);
        filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(filter)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal("{tab}\r\n{{ const r = run('d:/scripts/file.bat') }}\r\n", editor.BodyInput.Text);
        Assert.Equal("d:/scripts/file.bat", editor.BodyInput.SelectedText);
        editor.BodyInput.Text = "{tab}{{ const r = run('x.bat') }}{{ r.out }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal("(nichts)", editor.PreviewText.Text);
        Assert.Equal(Visibility.Visible, editor.PreviewNote.Visibility);
        Assert.StartsWith("run() startet hier nichts", editor.PreviewNote.Text);
        // Variablen aus den Blöcken davor gelten
        editor.BodyInput.Text = "{{ var n = 3; '' }}Wert: {{ n * 2 }}";
        editor.BodyInput.Select(editor.BodyInput.Text.Length - 4, 0);
        editor.UpdatePreview();
        Assert.Equal("6", editor.PreviewText.Text);
        Assert.Equal(Visibility.Collapsed, editor.PreviewNote.Visibility);
        filter.Text = "";
        Assert.Equal(["Tasten", "Ablauf", "Elemente und Fenster", "Skript"], Visible());

        // Fehlerhaftes Tag: Tooltip-Text an der Stelle, kein Tooltip daneben
        editor.BodyInput.Text = "Max{tab,x}";
        var view = editor.BodyInput.TextArea.TextView;
        view.EnsureVisualLines();
        var at = view.GetVisualPosition(new TextViewPosition(1, 7), VisualYPosition.LineMiddle) - view.ScrollOffset;
        Assert.Contains("{tab,3}", editor.Highlighting.ErrorAt(at));
        Assert.Null(editor.Highlighting.ErrorAt(view.GetVisualPosition(new TextViewPosition(1, 2), VisualYPosition.LineMiddle) - view.ScrollOffset));

        // Hover-Hilfe: erst der Fehler, dann Kurzbeschreibung und Hilfetext auf Deutsch; eine andere Taste nennt ihren Namen
        var help = SnippetHighlighting.HelpContent("bad", TagHelp.At("{tab,x}", 2));
        Assert.Equal(["bad", "Nächstes Feld"], help.Children.OfType<System.Windows.Controls.TextBlock>().Take(2).Select(t => t.Text));
        Assert.StartsWith("Drückt Tab", help.Children.OfType<System.Windows.Controls.TextBlock>().Last().Text);
        var other = TagHelp.At("{ctrl+s}", 2)!;
        Assert.Equal("Taste ctrl+s", SnippetHighlighting.HelpTitle(other));
        Assert.StartsWith("Drückt ctrl+s. Tasten verbinden sich mit +, z.B. {ctrl+s}", SnippetHighlighting.HelpText(other));
        Assert.EndsWith("dreimal Pfeil runter. Geht mit jedem Tasten-Tag, z.B. {tab,2}.", SnippetHighlighting.HelpText(TagHelp.At("{down,3}", 2)!));

        // Die Test-Taste zeigt, was läuft: alles, die Auswahl, das Tag oder das Skript an der Schreibmarke
        editor.BodyInput.Text = "Max{tab}{{ 6 * 7 }}";
        editor.BodyInput.Select(0, 0);
        Assert.Equal("Testen", editor.TestButton.Content);
        editor.BodyInput.Select(5, 0);
        Assert.Equal("Tag testen", editor.TestButton.Content);
        editor.BodyInput.Select(12, 0);
        Assert.Equal("Skript testen", editor.TestButton.Content);
        editor.BodyInput.Select(0, 2);
        Assert.Equal("Auswahl testen", editor.TestButton.Content);

        editor.BodyInput.Text = "{{ 1 + }}";
        editor.BodyInput.Select(3, 0);
        editor.UpdatePreview();
        Assert.Equal("Skriptfehler", editor.PreviewLabel.Text);
        Assert.StartsWith("Block 1: ", editor.PreviewText.Text);
        // Die Nummer zählt im ganzen Text, auch wenn ein Block davor fehlschlägt
        editor.BodyInput.Text = "{{ x( }}{{ 1 + }}";
        editor.BodyInput.Select(12, 0);
        editor.UpdatePreview();
        Assert.StartsWith("Block 2: ", editor.PreviewText.Text);

        editor.BodyInput.Text = "Grüsse aus Zürich";
        Assert.True(editor.Save());
        Assert.Equal(TextEncoding.Windows1252, TextFile.Read(ansi).Encoding);
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(ansi).Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("Gespeichert.", editor.StatusText.Text);
        Snapshot(editor, "editor-saved.png");
        Assert.NotNull(controller.Library.Find(@"Editor\Gruss.txt"));

        editor.NameInput.Text = "Gruss Zürich";
        Assert.True(editor.Save());
        Assert.False(File.Exists(ansi));
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt")).Text);
        Assert.NotNull(controller.Library.Find(@"Editor\Gruss Zürich.txt"));

        // Anderer Ordner: steht im Menü, macht den Baustein geändert, verschoben wird beim Speichern
        var archive = Directory.CreateDirectory(Path.Combine(dir.Path, @"structure\Editor\Archiv")).FullName;
        dir.File(@"structure\Editor\Archiv\Belegt.txt", "schon da");
        controller.Library.Reload();
        // Aufklappbar: "Editor" hat Unterordner, also ein Untermenü mit "Diesen Ordner wählen" zuoberst
        var folders = editor.FolderMenu().Items.OfType<System.Windows.Controls.MenuItem>().ToList();
        Assert.Equal("Bausteine", folders[0].Header);
        var editorFolder = folders.Single(i => (string)i.Tag == Path.Combine(dir.Path, @"structure\Editor"));
        Assert.Equal(FontWeights.SemiBold, editorFolder.FontWeight);
        var inEditor = editorFolder.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
        Assert.Equal(["Diesen Ordner wählen", "Archiv"], inEditor.Select(i => (string)i.Header));
        Assert.Equal(FontWeights.SemiBold, inEditor[0].FontWeight);
        Assert.Equal(FontWeights.Normal, inEditor[1].FontWeight);
        Assert.Equal(archive, inEditor[1].Tag);
        editor.ChooseFolder(archive);
        Assert.Equal("in Bausteine › Editor › Archiv", editor.FolderText.Text);
        Assert.True(editor.IsDirty);
        editor.NameInput.Text = "Belegt";
        Assert.False(editor.Save());
        Assert.Equal(Strings.NameExists, editor.StatusText.Text);
        editor.NameInput.Text = "Gruss Zürich";
        Assert.True(editor.Save());
        Assert.False(editor.IsDirty);
        Assert.False(File.Exists(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt")));
        Assert.Equal(Path.Combine(archive, "Gruss Zürich.txt"), editor.FilePath);
        Assert.Equal("Grüsse aus Zürich", TextFile.Read(editor.FilePath!).Text);
        Assert.NotNull(controller.Library.Find(@"Editor\Archiv\Gruss Zürich.txt"));
        editor.ChooseFolder(Path.GetDirectoryName(archive)!);
        Assert.True(editor.Save());
        Assert.Equal(Path.Combine(dir.Path, @"structure\Editor\Gruss Zürich.txt"), editor.FilePath);
        editor.Close();

        // Neuer Baustein: ohne Namen nicht speicherbar, dann als UTF-8 im Ordner
        var created = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, @"structure\Editor"));
        Assert.Equal("Neuer Baustein – Baustein bearbeiten", created.Title);
        created.BodyInput.Text = "Neu 😀";
        Assert.False(created.Save());
        Assert.Equal("Der Baustein braucht einen Namen.", created.StatusText.Text);
        created.NameInput.Text = "Gruss Zürich";
        Assert.False(created.Save());
        Assert.Equal(Strings.NameExists, created.StatusText.Text);
        created.NameInput.Text = "Neu";
        Assert.True(created.Save());
        Assert.Equal(new TextFileContent("Neu 😀", TextEncoding.Utf8), TextFile.Read(Path.Combine(dir.Path, @"structure\Editor\Neu.txt")));
        Assert.Equal(created.FilePath, Path.Combine(dir.Path, @"structure\Editor\Neu.txt"));
        created.Close();

        // Neuer Baustein in einem anderen Ordner als dem, aus dem er angelegt wurde
        var elsewhere = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, @"structure\Editor"));
        elsewhere.NameInput.Text = "Abgelegt";
        elsewhere.BodyInput.Text = "Archiviert";
        elsewhere.ChooseFolder(archive);
        Assert.True(elsewhere.Save());
        Assert.Equal(Path.Combine(archive, "Abgelegt.txt"), elsewhere.FilePath);
        elsewhere.Close();

        // Element-Picker: vor dem ersten Erfassen nur Fadenkreuz und leere Felder
        var picker = new ElementPickerWindow(controller);
        Assert.Equal("QuickHasteBot – Element erfassen", picker.Title);
        Assert.Equal(Visibility.Collapsed, picker.ResultPanel.Visibility);
        Assert.Equal("–", picker.IdText.Text);
        Assert.Equal(ElementAction.Click, ElementPickerWindow.DefaultAction("Button"));
        Assert.Equal(ElementAction.Set, ElementPickerWindow.DefaultAction("ComboBox"));
        Assert.Equal(ElementAction.Focus, ElementPickerWindow.DefaultAction("Edit"));
        Snapshot(picker, "element-picker.png");

        // Der Tag steht über die ganze Breite über den Knöpfen, ein langer umbricht statt abgeschnitten zu werden
        picker.ResultPanel.Visibility = Visibility.Visible;
        picker.TagOutput.Text = "{window,process=chrome}{click,name=Zeit;type=button;window=process=chrome}";
        picker.UpdateLayout();
        Snapshot(picker, "element-picker-result.png");
        Assert.Equal(picker.ResultPanel.ActualWidth, picker.TagOutput.ActualWidth, 1);
        Assert.True(picker.CopyButton.TranslatePoint(new Point(), picker.TagOutput).Y >= picker.TagOutput.ActualHeight);
        Assert.True(picker.TagOutput.ActualHeight > 2 * picker.TagOutput.FontSize * 1.2, "langer Tag umbrochen");

        // Fünf Aktionen in einer Reihe, auch die langen deutschen Wörter
        System.Windows.Controls.RadioButton[] actions =
            [picker.ActionFocus, picker.ActionClick, picker.ActionDoubleClick, picker.ActionRightClick, picker.ActionSet];
        Assert.Equal(["Fokus", "Klick", "Doppelklick", "Rechtsklick", "Setzen"], actions.Select(a => (string)a.Content));
        Assert.All(actions, a => Assert.Equal(0, a.TranslatePoint(new Point(), picker.ActionFocus).Y, 1));
        Assert.True(picker.ActionSet.TranslatePoint(new Point(picker.ActionSet.ActualWidth, 0), picker.ResultPanel).X <= picker.ResultPanel.ActualWidth);
        picker.ActionDoubleClick.IsChecked = true;
        Assert.Equal(ElementAction.DoubleClick, picker.SelectedAction);
        picker.ActionRightClick.IsChecked = true;
        Assert.Equal(ElementAction.RightClick, picker.SelectedAction);

        // Ohne Editor ist Kopieren der blaue Knopf; mit Editor links blau „In Editor einfügen“, rechts Kopieren und Testen
        picker.UpdateInsertButton();
        Assert.Equal(Visibility.Collapsed, picker.InsertButton.Visibility);
        Assert.Same(picker.FindResource("AccentButtonStyle"), picker.CopyButton.Style);
        picker.InsertButton.Visibility = Visibility.Visible;
        picker.UpdateLayout();
        double X(FrameworkElement button) => button.TranslatePoint(new Point(), picker.ResultPanel).X;
        Assert.True(X(picker.InsertButton) < X(picker.CopyButton) && X(picker.CopyButton) < X(picker.TestButton));
        Assert.Same(picker.FindResource("AccentButtonStyle"), picker.InsertButton.Style);
        Snapshot(picker, "element-picker-actions.png");
        picker.Close();

        // Fehler: rot, nichts zu kopieren
        ai.AnswerInput.Text = "{{ 1 + }}";
        ai.TestAnswer();
        Assert.StartsWith("Block 1: ", ai.TestResult.Text);
        Assert.Equal(Visibility.Collapsed, ai.CopyRow.Visibility);
        ai.AnswerInput.Text = "";
        ai.TestAnswer();
        Assert.Equal(Visibility.Collapsed, ai.TestResultBox.Visibility);
        Assert.Equal(Visibility.Visible, ai.AnswerPlaceholder.Visibility);
        // Der Dienst lässt sich hier wählen, gilt sofort und wird gespeichert
        ai.ServiceInput.SelectedItem = ScriptPrompt.Services.Single(s => s.Name == "Perplexity");
        Assert.Equal("In Perplexity öffnen", ai.OpenButton.Content);
        Assert.Contains(@"""AiPromptUrl"": ""https://www.perplexity.ai/search?q={prompt}""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        ai.Close();

        // Vorlage: wird beim ersten Mal mit dem eingebauten Prompt angelegt, eine eigene gilt für die Vorschau
        var template = controller.CreateAiPromptTemplate();
        Assert.Equal(ScriptPrompt.DefaultTemplate, File.ReadAllText(template));
        File.WriteAllText(template, "Mach mir JS: {task}");
        Assert.Equal(template, controller.CreateAiPromptTemplate());
        Assert.Equal("Mach mir JS: {task}", File.ReadAllText(template));
        var own = new AiPromptWindow(controller);
        Assert.Equal("Mach mir JS: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        // Zurücksetzen gibt es nur mit eigener Vorlage. Ohne Datei gilt wieder der eingebaute Prompt.
        Assert.Equal(Visibility.Visible, own.ResetTemplateLink.Visibility);
        File.Delete(template); // im Programm über den Papierkorb, siehe ResetAiPromptTemplate
        own.RefreshTemplate();
        Assert.Equal(Visibility.Collapsed, own.ResetTemplateLink.Visibility);
        Assert.StartsWith("Write JavaScript code for this task: Datum in 14 Tagen mit Wochentag", own.PromptPreview.Text);
        controller.ResetAiPromptTemplate(0); // ohne Datei nichts zu tun
        Assert.False(controller.HasAiPromptTemplate);
        own.Close();

        var settings = new SettingsWindow(controller);
        Assert.Equal(PopupPosition.TopRight, settings.PositionInput.Position);
        // KI-Dienst: zeigt die Wahl aus dem Prompt-Fenster, eine Auswahl hier gilt ebenso sofort
        Assert.Equal("Perplexity", settings.AiServiceInput.SelectedItem?.ToString());
        Assert.Equal(3, settings.AiServiceInput.Items.Count);
        settings.AiServiceInput.SelectedIndex = 0;
        Assert.Equal(ScriptPrompt.DefaultUrl, controller.Settings.AiPromptUrl);
        Snapshot(settings, "settings.png");
        settings.SettingsTabs.SelectedIndex = 1;
        Snapshot(settings, "settings-popup.png");
        // Eine Auswahl im Raster gilt sofort und wird gespeichert
        settings.PositionInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings)!, 0, Key.NumPad1) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.Equal(PopupPosition.BottomLeft, controller.Settings.PopupPosition);
        Assert.Contains(@"""PopupPosition"": ""BottomLeft""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));

        // Zurück zum ersten Tab: Der Hotkey darf dabei nicht den Fokus bekommen, sonst wartet er auf eine neue Kombination
        settings.Activate();
        var general = (System.Windows.Controls.TabItem)settings.SettingsTabs.Items[0];
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        general.RaiseEvent(click);
        if (!click.Handled)
            general.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        settings.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        Assert.Equal(0, settings.SettingsTabs.SelectedIndex);
        Assert.False(settings.HotkeyInput.IsKeyboardFocused);

        settings.Close();
        popup.Close();
    }

    /// <summary>Dieselben Fenster mit englischen Texten, eingestellt über settings.json.</summary>
    static void English()
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\5-Links\Intranet.url", "[InternetShortcut]\r\nURL=https://intranet.example.com");
        dir.File(@"structure\Phone numbers.txt", "Reception 100");
        Directory.CreateDirectory(Path.Combine(dir.Path, @"structure\Empty"));
        dir.File("settings.json", """{ "Language": "en", "ListColumnShare": 0.5, "PopupWidth": 100 }""");

        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["Intranet"], viewModel.DetailItems.Select(i => i.Snippet.Name));

        // Zu schmal gespeichert: so breit wie die Zeile mit den Tastenkürzeln, sonst die gespeicherte Breite
        popup.ApplyWidth();
        Assert.Equal(popup.MinWidth, popup.Width);
        // Die Aufteilung von früher (Anteil) wird einmal in eine feste Breite umgerechnet und sieht gleich aus
        Assert.Null(controller.Settings.ListColumnShare);
        Assert.DoesNotContain("ListColumnShare", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        Snapshot(popup, "popup-narrow.png");
        Assert.Equal(popup.ListColumn.ActualWidth, popup.DetailColumn.ActualWidth, tolerance: 1.0);
        var listWidth = popup.ListColumn.ActualWidth;
        Assert.Equal(listWidth, controller.Settings.ListColumnWidth, 1);
        Assert.InRange(popup.MinWidth, 300, 600);
        // Die ganze Zeile hat Platz, auch "Ctrl , Settings" am Ende
        Assert.True(popup.HintBar.ActualWidth >= popup.HintBar.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right) - 0.5);
        controller.Settings.PopupWidth = 900;
        popup.ApplyWidth();
        Assert.Equal(900, popup.Width);
        Snapshot(popup, "popup-en.png");
        // Breiter: die Liste links bleibt, der Platz geht nach rechts
        Assert.Equal(listWidth, popup.ListColumn.ActualWidth, 1);
        Assert.True(popup.DetailColumn.ActualWidth > listWidth + 300);

        // Zu schmal für die gespeicherte Liste: nur in der Anzeige schmaler, rechts bleibt die Mindestbreite
        controller.Settings.ListColumnWidth = 860;
        popup.ApplyListWidth();
        popup.UpdateLayout();
        Assert.Equal(popup.DetailColumn.MinWidth, popup.DetailColumn.ActualWidth, 1);
        Assert.Equal(860, controller.Settings.ListColumnWidth);
        controller.Settings.ListColumnWidth = listWidth;
        popup.ApplyListWidth();

        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "Empty");
        Assert.False(viewModel.ShowDetailItems);
        Assert.Equal("This folder is empty. Ctrl+E opens it in Explorer.", viewModel.DetailText);

        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "5-Links").Snippet);
        Assert.Equal(["Snippets", "5-Links"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("Opens https://intranet.example.com", viewModel.DetailText);

        // Ohne Treffer bleibt rechts nichts vom letzten Eintrag stehen
        viewModel.Query = "xyz";
        Assert.Equal("0 matches", viewModel.Breadcrumbs.Single().Name);
        Assert.Empty(viewModel.DetailItems);
        Assert.Equal("", viewModel.DetailText);

        // Eine von Hand eingetragene Adresse erscheint als eigener Eintrag
        controller.Settings.AiPromptUrl = "https://claude.ai/new?q={prompt}";
        var settings = new SettingsWindow(controller);
        Assert.Equal("QuickHasteBot – Settings", settings.Title);
        Assert.Equal(4, settings.AiServiceInput.Items.Count);
        Assert.Equal("Custom address", settings.AiServiceInput.SelectedItem?.ToString());
        var customAi = new AiPromptWindow(controller);
        Assert.Equal("Open in browser", customAi.OpenButton.Content);
        customAi.Close();
        Snapshot(settings, "settings-en.png");
        settings.Close();
        popup.Close();
    }

    /// <summary>Sprachwechsel ohne Neustart: das Einstellungsfenster kommt an derselben Stelle in der neuen Sprache wieder.</summary>
    static void SwitchLanguage()
    {
        using var dir = new TempDirectory();
        using var controller = new AppController(new AppPaths(dir.Path));
        SettingsWindow Current() => Application.Current.Windows.OfType<SettingsWindow>().Single();

        // Erster Start: structure mit den Beispielen in der Sprache der Oberfläche, alle im Ordner Demo
        var structure = Path.Combine(dir.Path, "structure");
        var demo = Path.Combine(structure, "Demo");
        Assert.Equal(demo, controller.DemoRoot);
        Assert.Equal([demo], Directory.GetFileSystemEntries(structure));
        Assert.True(File.Exists(Path.Combine(demo, "Willkommen.txt")));
        Assert.True(File.Exists(Path.Combine(demo, "6 - Skripte", "JavaScript-Blöcke {{ }}.txt")));
        // {include} im Beispiel findet seinen Baustein auch unter Demo
        Assert.Equal(Path.Combine(demo, "1 - Einfacher Text", "Freundliche Grüsse.txt"),
            controller.Library.FindIncluded("Einfacher Text/Freundliche Grüsse")?.FullPath);

        // Neu aufbauen (Einstellungen): ohne Demo gibt es nichts für den Papierkorb, der Ordner entsteht und wird gleich eingelesen
        Directory.Delete(demo, recursive: true);
        File.WriteAllText(Path.Combine(structure, "Eigener.txt"), "eigen");
        Assert.Equal(SampleStructure.Files("de").Count, controller.ExportSamples(demo, rebuild: true, 0));
        Assert.NotNull(controller.Library.FindIncluded("Willkommen"));
        Assert.True(File.Exists(Path.Combine(structure, "Eigener.txt")));

        // Anderer Ordner: die Beispiele direkt hinein, eigene Dateien bleiben
        var other = Path.Combine(dir.Path, "Anderswo");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Eigener.txt"), "eigen");
        controller.ExportSamples(other, rebuild: false, 0);
        Assert.True(File.Exists(Path.Combine(other, "Willkommen.txt")));
        Assert.True(File.Exists(Path.Combine(other, "Eigener.txt")));

        controller.ShowSettings(new Point(-20000, -20000));
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);

        // Das KI-Fenster gibt es nur einmal, sein Text überlebt Schliessen und Sprachwechsel
        AiPromptWindow Prompt() => Application.Current.Windows.OfType<AiPromptWindow>().Single();
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Prompt().RequirementInput.Text = "Zufälliger Satz";
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Assert.Equal("Skriptblock mit KI", Prompt().Title);

        controller.ChangeLanguage("en");
        Assert.Equal("QuickHasteBot – Settings", Current().Title);
        Assert.Equal("Script block with AI", Prompt().Title);
        Assert.Equal("Zufälliger Satz", Prompt().RequirementInput.Text);
        Assert.Equal(-20000, Prompt().Left);
        Prompt().Close();
        controller.ShowAiPrompt(new Point(-20000, -20000));
        Assert.Equal("Zufälliger Satz", Prompt().RequirementInput.Text);
        Prompt().Close();
        // Gespeichert, steht also auch nach einem Neustart wieder da
        using (var restarted = new AppController(new AppPaths(dir.Path)))
            Assert.Equal("Zufälliger Satz", restarted.Settings.AiPromptText);
        Assert.Equal(-20000, Current().Left);
        Assert.Equal("Snippets", Strings.BreadcrumbRoot);
        Assert.Equal("en", controller.Settings.Language);

        controller.ChangeLanguage("de");
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);
        Current().Close();
    }

    /// <summary>Hell/dunkel aus den Einstellungen gilt sofort, die eigenen Meldungen folgen dem Theme.</summary>
    static void ThemeAndDialogs()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de", "Theme": "Dark" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        Assert.Equal(AppTheme.Dark, controller.Settings.Theme);
        try
        {
            var settings = new SettingsWindow(controller);
            Assert.Equal("Dunkel", settings.ThemeInput.SelectedItem?.ToString());
            Assert.Equal(["Automatisch (Windows)", "Hell", "Dunkel"], settings.ThemeInput.Items.Cast<object>().Select(i => i.ToString()));
            settings.ThemeInput.SelectedIndex = 1;
            Assert.Equal(ThemeMode.Light, Application.Current.ThemeMode);
            Assert.Contains(@"""Theme"": ""Light""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
            settings.ThemeInput.SelectedIndex = 2;
            Assert.Equal(ThemeMode.Dark, Application.Current.ThemeMode);
            Snapshot(settings, "settings-dark.png");
            settings.Close();

            // Löschen fragt mit Ja/Nein, vorgewählt und mit Esc: Nein
            var delete = new MessageDialog(string.Format(Strings.DeleteConfirmFormat, "Gruss.txt"), MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No);
            Snapshot(delete, "dialog-dark.png");
            var buttons = delete.Buttons.Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.Equal(["Ja", "Nein"], buttons.Select(b => b.Content));
            Assert.True(buttons[1].IsDefault);
            Assert.True(buttons[1].IsCancel);
            Assert.Equal(MessageBoxResult.No, delete.Result);
            Assert.Equal(Visibility.Visible, delete.Symbol.Visibility);
            buttons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(MessageBoxResult.Yes, delete.Result);
            Assert.False(delete.IsVisible);

            // Nur OK, ohne Symbol: Esc ergibt OK
            controller.ChangeTheme(AppTheme.Light);
            var info = new MessageDialog("Hinweis", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);
            Snapshot(info, "dialog-light.png");
            Assert.Equal("OK", info.Buttons.Children.OfType<System.Windows.Controls.Button>().Single().Content);
            Assert.Equal(MessageBoxResult.OK, info.Result);
            Assert.Equal(Visibility.Collapsed, info.Symbol.Visibility);
            info.Close();
        }
        finally
        {
            // Die übrigen Tests rechnen mit dem hellen Theme
            UiTheme.Apply(AppTheme.Light);
        }
    }

    /// <summary>Hotkeys im Dateinamen: Badge im Popup, Konflikte im Dialog, beim Umbenennen und in den Einstellungen.</summary>
    static void FileHotkeys()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        dir.File(@"structure\3-SQL [Ctrl+Alt+S]\Audit [Ctrl+Alt+A].txt", "insert");
        dir.File(@"structure\Gruss [Ctrl+Alt+G].txt", "Gruss");
        dir.File(@"structure\Gutschein [Ctrl+Alt+G].txt", "Gutschein");
        dir.File(@"structure\Popup [Ctrl+Shift+Y].txt", "Popup");
        dir.File(@"structure\Notiz.txt", "Notiz");
        using var controller = new AppController(new AppPaths(dir.Path));
        controller.SyncFileHotkeys(announce: false);

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["3-SQL", "Gruss", "Gutschein", "Notiz", "Popup"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal(["Ctrl+Alt+S", "Ctrl+Alt+G", "Ctrl+Alt+G", null, "Ctrl+Shift+Y"], viewModel.Items.Select(i => i.HotkeyText));
        Assert.Equal([false, false, true, false, true], viewModel.Items.Select(i => i.HasHotkeyProblem));
        Assert.Equal("Schon vergeben an «Gruss».", viewModel.Items[2].HotkeyTooltip);
        Assert.Equal("Hotkey Ctrl+Alt+S, gilt in jedem Programm", viewModel.Items[0].HotkeyTooltip);
        Snapshot(popup, "popup-hotkeys.png");

        // Hotkey eines Ordners: das Popup beginnt dort, die Kopfzeile ohne Tag
        viewModel.Reset(controller.Library.All.Single(s => s.Name == "3-SQL").RelativePath);
        Assert.Equal(["Bausteine", "3-SQL"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["Audit"], viewModel.Items.Select(i => i.Snippet.Name));
        popup.Close();

        // Dialog: vergebene Kombinationen sperren, eine freie zeigt den neuen Dateinamen
        var notiz = controller.Library.All.Single(s => s.Name == "Notiz");
        var dialog = new HotkeyDialog(controller, notiz);
        Assert.False(dialog.RemoveButton.IsEnabled);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.HotkeyInput.Gesture = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.A);
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal("Schon vergeben an «Audit» (3-SQL).", dialog.Problem.Text);
        Assert.False(dialog.ConfirmButton.IsEnabled);
        dialog.HotkeyInput.Gesture = HotkeyGesture.Default;
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal(Strings.FileHotkeyIsMain, dialog.Problem.Text);
        dialog.HotkeyInput.Gesture = new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.N);
        dialog.HotkeyInput.RaiseCaptured();
        Assert.Equal("", dialog.Problem.Text);
        Assert.True(dialog.ConfirmButton.IsEnabled);
        Assert.Equal("Dateiname: Notiz [Ctrl+Alt+N].txt", dialog.FileNameText.Text);
        Snapshot(dialog, "hotkey-dialog.png");
        dialog.Close();

        // Umbenennen mit einem vergebenen Hotkey im Namen ist gesperrt, mit dem eigenen nicht
        var gruss = controller.Library.All.Single(s => s.Name == "Gruss");
        var rename = NameDialog.ForRename(gruss, name => controller.HotkeyConflictForName(name, false, gruss.FullPath, gruss.Hotkey));
        rename.NameInput.Text = "Grüsse [Ctrl+Alt+G].txt";
        Assert.Equal("", rename.Problem.Text);
        Assert.True(rename.ConfirmButton.IsEnabled);
        rename.NameInput.Text = "Gruss [Ctrl+Alt+S].txt";
        Assert.Equal("Schon vergeben an «3-SQL».", rename.Problem.Text);
        Assert.False(rename.ConfirmButton.IsEnabled);
        rename.Close();

        // Neuer Ordner: der Hinweis nennt den offenen Ordner, vergebene Namen und Hotkeys sperren
        var sql = controller.Library.All.Single(s => s.Name == "3-SQL");
        var newFolder = NameDialog.ForNewFolder(sql.FullPath, "Bausteine › 3-SQL",
            name => controller.HotkeyConflictForName(name, true, null, null));
        Assert.Equal("Neuer Ordner", newFolder.FileName);
        Assert.Equal("Neue Ordner entstehen immer im Ordner, der im Popup gerade offen ist: Bausteine › 3-SQL", newFolder.Hint.Text);
        Assert.Equal(Visibility.Visible, newFolder.Hint.Visibility);
        Assert.True(newFolder.ConfirmButton.IsEnabled);
        Snapshot(newFolder, "new-folder.png");
        newFolder.NameInput.Text = "Audit [Ctrl+Alt+A].txt";
        Assert.Equal(Strings.NameExists, newFolder.Problem.Text);
        newFolder.NameInput.Text = "Archiv [Ctrl+Alt+G]";
        Assert.Equal("Schon vergeben an «Gruss».", newFolder.Problem.Text);
        Assert.False(newFolder.ConfirmButton.IsEnabled);
        newFolder.Close();

        // Einstellungen: alle Hotkeys, die zwei Konflikte rot
        var settings = new SettingsWindow(controller);
        settings.ShowFileHotkeys();
        var rows = settings.FileHotkeysList.Items.Cast<SettingsWindow.FileHotkeyRow>().ToList();
        Assert.Equal(["Ctrl+Alt+S", "Ctrl+Alt+A", "Ctrl+Alt+G", "Ctrl+Alt+G", "Ctrl+Shift+Y"], rows.Select(r => r.Hotkey));
        Assert.Equal(2, rows.Count(r => r.IsProblem));
        Assert.Equal("3 aktiv, 2 wirken nicht", settings.FileHotkeysSummary.Text);
        Snapshot(settings, "settings-hotkeys.png");
        settings.Close();
    }

    /// <summary>
    /// Ein langer Ordnerinhalt rechts macht das Popup höher statt zu scrollen. Es schrumpft nicht, solange die linke
    /// Liste bleibt, und hält sich an die maximale Höhe (dann scrollt die rechte Liste doch).
    /// </summary>
    static void PopupGrowsWithFolderList()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        dir.File(@"structure\1-Kurz\Eins.txt", "eins");
        for (var i = 1; i <= 25; i++)
            dir.File($@"structure\2-Lang\Text {i:00}.txt", $"text {i}");
        dir.File(@"structure\3-Notiz.txt", "notiz");
        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller) { MaxHeight = 3000 };
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal("1-Kurz", viewModel.Selected?.Snippet.Name);
        Snapshot(popup, "popup-grow-short.png");
        var shortHeight = popup.ActualHeight;
        var detailScroll = FindDescendant<System.Windows.Controls.ScrollViewer>(popup.DetailList);

        // Der lange Ordner passt ganz hinein, ohne Scrollbalken. Höher als der Bildschirm lässt Windows kein Fenster
        // werden (manche CI-Runner haben nur 768 px): stösst das Popup dort an, muss die Liste scrollen statt abzuschneiden
        Select("2-Lang");
        Assert.True(popup.ActualHeight > shortHeight + 200, $"{popup.ActualHeight} nicht höher als {shortHeight}");
        Assert.Equal(25, popup.DetailList.Items.Count);
        var screenFull = popup.ActualHeight >= SystemParameters.MaximumWindowTrackHeight - 2;
        Assert.True(screenFull == detailScroll.ScrollableHeight > 0,
            $"Liste scrollt um {detailScroll.ScrollableHeight}, Popup {popup.ActualHeight} hoch, höchstens {SystemParameters.MaximumWindowTrackHeight}");
        Snapshot(popup, "popup-grow-long.png");
        var longHeight = popup.ActualHeight;

        // Zurück auf kurze Inhalte: die Höhe bleibt, sonst rutschte die Liste unter der Maus weg
        Select("1-Kurz");
        Assert.Equal(longHeight, popup.ActualHeight);
        Select("3-Notiz");
        Assert.Equal(longHeight, popup.ActualHeight);

        // Eine neue Liste links gibt die Höhe wieder frei
        viewModel.Query = "eins";
        viewModel.Query = "";
        popup.UpdateLayout();
        Assert.Equal("1-Kurz", viewModel.Selected?.Snippet.Name);
        Assert.Equal(shortHeight, popup.ActualHeight);

        // Höher als erlaubt wird es nie, dann scrollt die rechte Liste (und das Layout bleibt stabil)
        popup.MaxHeight = shortHeight + 100;
        Select("2-Lang");
        Assert.Equal(shortHeight + 100, popup.ActualHeight, 1.0);
        Assert.True(detailScroll.ScrollableHeight > 0);

        // Mit gespeicherter Höhe springt nichts: kurze und lange Ordner gleich hoch, der lange scrollt. Fensterhöhen
        // vergleichen die Tests auf 1 px genau, Windows rundet sie auf ganze Bildschirmpixel (bei 96 DPI 417.58 -> 418)
        popup.MaxHeight = 3000;
        popup.UpdateLayout();
        Assert.True(popup.ActualHeight > shortHeight + 150, $"{popup.ActualHeight} nicht wieder gewachsen");
        controller.Settings.PopupHeight = shortHeight + 60;
        popup.ApplyHeight();
        // Kleiner als der gewachsene Ordner rechts: der Scrollbalken kommt sofort, nicht erst mit der nächsten Liste
        popup.UpdateLayout();
        Assert.Equal(shortHeight + 60, popup.ActualHeight, 1.0);
        Assert.True(detailScroll.ScrollableHeight > 0, "Ordnerliste rechts scrollt nach dem Verkleinern nicht");
        Select("1-Kurz");
        Assert.Equal(shortHeight + 60, popup.ActualHeight, 1.0);
        Select("2-Lang");
        Assert.Equal(shortHeight + 60, popup.ActualHeight, 1.0);
        Assert.True(detailScroll.ScrollableHeight > 0);
        Snapshot(popup, "popup-fixed-height.png");

        // Von Hand zu klein eingetragen: Suchfeld, Fusszeile und die Mindesthöhe der Spalten haben trotzdem Platz
        controller.Settings.PopupHeight = 50;
        popup.ApplyHeight();
        popup.UpdateLayout();
        Assert.Equal(popup.Columns.MinHeight, popup.Columns.ActualHeight, 1.0);
        Assert.True(popup.HintBar.ActualHeight > 0);
        var hintBottom = popup.HintBar.TranslatePoint(new Point(0, popup.HintBar.ActualHeight), popup).Y;
        Assert.True(hintBottom <= popup.ActualHeight, $"Fusszeile endet bei {hintBottom}, Popup {popup.ActualHeight} hoch");

        // Doppelklick auf den Rand: wieder so hoch wie die Liste, auch gespeichert
        popup.BottomEdge.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent,
        });
        Assert.Null(controller.Settings.PopupHeight);
        Assert.Contains("\"PopupHeight\": null", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Query = "eins";
        viewModel.Query = "";
        popup.UpdateLayout();
        Assert.Equal(shortHeight, popup.ActualHeight, 1.0);
        popup.Close();

        void Select(string name)
        {
            viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == name);
            popup.UpdateLayout();
        }
    }

    /// <summary>ask(): alle Fragen in einem Fenster, Vorgabe vor letzter Antwort. Und der Hinweis auf eine neue Version.</summary>
    static void AskDialogAndUpdateLink()
    {
        UiLanguage.Apply("de");
        var dialog = new AskDialog("Offerte", [new Question("Kunde", "Muster"), new Question("Betreff", "")], new AskWindowOptions(),
            new Dictionary<string, string> { ["Kunde"] = "Meier", ["Betreff"] = "Anfrage" });
        Snapshot(dialog, "ask.png");
        Assert.Equal("Offerte – QuickHasteBot", dialog.Title);
        Assert.Equal("Einfügen", dialog.ConfirmButton.Content);
        Assert.Equal("«Offerte» braucht vor dem Einfügen noch ein paar Angaben:", dialog.Prompt.Text);
        Assert.Equal(Visibility.Visible, dialog.Symbol.Visibility);
        Assert.Equal(["Muster", "Anfrage"], dialog.Inputs.Select(i => i.Text));
        dialog.Inputs[1].Text = "Rückfrage";
        Assert.Equal("Rückfrage", dialog.Answers["Betreff"]);
        dialog.Close();

        // Eigener Knopf, Titel und Text aus den Optionen von ask()
        dialog = new AskDialog("Orakel", [new Question("Deine Frage", "Soll ich?")],
            new AskWindowOptions("Orakel befragen", "Das Orakel", "Was möchtest du wissen?"), new Dictionary<string, string>());
        Snapshot(dialog, "ask-options.png");
        Assert.Equal("Das Orakel", dialog.Title);
        Assert.Equal("Orakel befragen", dialog.ConfirmButton.Content);
        Assert.Equal("Was möchtest du wissen?", dialog.Prompt.Text);
        dialog.Close();

        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de", "LatestRelease": "v99.0.0" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var settings = new SettingsWindow(controller);
        Snapshot(settings, "settings-update.png");
        var link = (System.Windows.Controls.TextBlock)settings.FindName("UpdateLink");
        // Ohne git (Version 0.0.0) gibt es keinen Vergleich, also auch keinen Hinweis
        if (AppVersion.Numeric(AppVersion.Informational) is null)
            Assert.Equal(Visibility.Collapsed, link.Visibility);
        else
        {
            Assert.Equal(Visibility.Visible, link.Visibility);
            Assert.Equal("Version 99.0.0 herunterladen", link.Text);
        }
        Assert.True(((System.Windows.Controls.CheckBox)settings.FindName("CheckForUpdatesInput")).IsChecked);
        settings.Close();
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
        var template = AppController.CreateTemplate(controller.HasteScriptTemplate);
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
        Assert.Equal("{window,process=chrome}{click,name=Zeit}", controller.Preview(body.Text).Text);

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

    /// <summary>{set} bei einem Auswahlfeld, dessen Liste nicht unter dem Feld steht, sondern woanders im Fenster.</summary>
    static void SetPicksFromAListElsewhere()
    {
        var list = new System.Windows.Controls.ListBox { Name = "liste" };
        foreach (var option in new[] { "3", "4", "5" })
            list.Items.Add(new System.Windows.Controls.ListBoxItem { Content = option });
        var box = new PopupSelect(list) { Name = "personen", Width = 120 };
        var window = new Window
        {
            Width = 300, Height = 300, ShowInTaskbar = false,
            Content = new System.Windows.Controls.StackPanel
            {
                Children = { box, new System.Windows.Controls.TextBlock { Name = "hinweis", Text = "Weiter unten:" }, list },
            },
        };
        Snapshot(window, "popup-select.png");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        // UIA ruft zurück in dieses Fenster: auf einem anderen Thread, der UI-Thread läuft weiter
        ElementResult Run(string tag)
        {
            Assert.True(KeyTags.TryParse(tag, null, out var steps, out var error), error);
            var run = Task.Run(() => ElementAutomation.Run((ElementStep)steps[0], hwnd, () => false));
            Assert.True(PumpUntil(() => run.IsCompleted, TimeSpan.FromSeconds(15)));
            return run.Result;
        }
        ElementResult Set(string value) => Run($"set,#personen,\"{value}\"");

        // Das Fenster steht ausserhalb des Bildschirms (Snapshot). Ein echter Klick landete am Bildschirmrand in einem
        // fremden Fenster, darum wird gar nicht erst geklickt.
        var offscreen = Run("click,#hinweis");
        Assert.Equal(ElementOutcome.Failed, offscreen.Outcome);
        Assert.Contains("outside the screen", offscreen.Error);

        Assert.Equal(ElementOutcome.Done, Set("5").Outcome);
        Assert.Equal("5", box.Text);
        Assert.False(box.IsOpen);

        // Gibt es die Option nicht, wird das gemeldet statt ins schreibgeschützte Feld getippt
        var missing = Set("9");
        Assert.Equal(ElementOutcome.Failed, missing.Outcome);
        Assert.Contains("\"9\"", missing.Error);
        Assert.Equal("5", box.Text);
        Assert.False(box.IsOpen);
        window.Close();
    }

    /// <summary>
    /// Mit Schloss ist das Popup ein normales Fenster (nicht Topmost), und Fensterwechsel rühren seine Stelle in der
    /// Reihenfolge nicht an: ob Editor, Popup oder ein fremdes Programm vorne ist. Ein fremdes Programm wird zum Ziel.
    /// Das Schloss wird gespeichert.
    /// </summary>
    static void KeptOpenPopupIsAnOrdinaryWindow()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var popup = new SearchPopup(controller);
        Snapshot(popup, "popup-kept-open.png");
        Assert.False(popup.KeepOpen);
        Assert.True(popup.Topmost);
        popup.KeepOpenToggle.IsChecked = true;
        Assert.False(popup.Topmost);
        Assert.True(controller.Settings.KeepPopupOpen);
        Assert.Contains("\"KeepPopupOpen\": true", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        var editor = new Window { Width = 200, Height = 100, ShowInTaskbar = false };
        Snapshot(editor, "popup-kept-open-editor.png");
        var popupHwnd = new System.Windows.Interop.WindowInteropHelper(popup).Handle;
        var editorHwnd = new System.Windows.Interop.WindowInteropHelper(editor).Handle;
        var foreign = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = 0, Y = 0 });
        NativeMethods.GetWindowThreadProcessId(foreign, out var foreignPid);
        Assert.NotEqual((uint)Environment.ProcessId, foreignPid);

        var behind = NativeMethods.GetWindow(popupHwnd, NativeMethods.GW_HWNDNEXT);
        var target = popup.TargetWindow;
        foreach (var window in new[] { editorHwnd, popupHwnd, foreign, editorHwnd })
        {
            popup.OnForegroundChanged(window);
            Assert.True(popup.IsVisible, "offen gelassen");
            Assert.False(popup.Topmost);
            Assert.Equal(behind, NativeMethods.GetWindow(popupHwnd, NativeMethods.GW_HWNDNEXT));
            if (window == foreign)
                target = foreign;
            Assert.Equal(target, popup.TargetWindow); // eigene Fenster werden nie zum Ziel
        }

        // Ein neues Popup (nächster Start) übernimmt das Schloss
        var next = new SearchPopup(controller);
        Assert.True(next.KeepOpen);
        next.Close();

        // Schloss auf: wieder zuoberst wie das Popup ohne Schloss, und gespeichert
        popup.KeepOpenToggle.IsChecked = false;
        Assert.True(popup.Topmost);
        Assert.False(controller.Settings.KeepPopupOpen);
        editor.Close();
        popup.Close();
    }

    /// <summary>
    /// name= mit *, help= und "Container > Element" gegen echte UIA: zweimal "Strasse" in zwei Gruppen, ein Posteingang
    /// mit wechselndem Zähler, ein Symbol-Button nur mit Tooltip. Der Picker schlägt für die zweite Strasse den Container vor.
    /// </summary>
    static void ContainersWildcardsAndTooltips()
    {
        var clicked = "";
        System.Windows.Controls.Button Button(string content, string tag, string? tooltip = null)
        {
            var b = new System.Windows.Controls.Button { Content = content, ToolTip = tooltip };
            b.Click += (_, _) => clicked = tag;
            return b;
        }
        System.Windows.Controls.GroupBox Group(string header, params UIElement[] children)
        {
            var panel = new System.Windows.Controls.StackPanel();
            foreach (var child in children) panel.Children.Add(child);
            return new System.Windows.Controls.GroupBox { Header = header, Content = panel };
        }

        var inbox = Button("Posteingang - 2 ungelesene Elemente", "inbox");
        var window = new Window
        {
            Width = 300, Height = 400, ShowInTaskbar = false,
            Content = new System.Windows.Controls.StackPanel
            {
                Children =
                {
                    Group("Rechnungsadresse", Button("Strasse", "rechnung")),
                    Group("Lieferadresse", Button("Strasse", "liefer")),
                    inbox,
                    Button("🖶", "drucken", "Drucken (Strg+P)"),
                },
            },
        };
        Snapshot(window, "containers.png");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        ElementResult Run(string tag)
        {
            clicked = "";
            Assert.True(KeyTags.TryParse(tag.Trim('{', '}'), null, out var steps, out var error), error);
            var run = Task.Run(() => ElementAutomation.Run((ElementStep)steps[0], hwnd, () => false));
            Assert.True(PumpUntil(() => run.IsCompleted, TimeSpan.FromSeconds(20), pause: 1));
            return run.Result;
        }
        void Clicks(string tag, string expected)
        {
            var result = Run(tag);
            Assert.True(result.Outcome == ElementOutcome.Done, $"{tag}: {result.Outcome} {result.Error}");
            Assert.True(PumpUntil(() => clicked == expected, TimeSpan.FromSeconds(5)), $"{tag}: {clicked}");
        }

        Clicks("{click,name=Strasse}", "rechnung");
        Clicks("{click,name=Lieferadresse > name=Strasse;type=button}", "liefer");
        // index zählt über alle Container. type=, weil in WPF auch der Text im Button "Strasse" heisst
        Clicks("{click,name=*adresse > name=Strasse;type=button;index=2}", "liefer");
        Clicks("{click,name=*adresse;index=2 > name=Strasse}", "liefer");        // index beim Container: der zweite
        Clicks("{click,name=Posteingang*;type=button}", "inbox");
        inbox.Content = "Posteingang";
        Clicks("{click,name=Posteingang*;type=button}", "inbox");
        Clicks("{click,help=Drucken*}", "drucken");
        Clicks("{click,help=drucken (strg+p);type=button}", "drucken");
        Assert.Equal(ElementOutcome.NotFound, Run("{click,name=Rechnungsadresse > name=Posteingang*}").Outcome);

        // Picker: "Strasse" allein trifft die erste, für die zweite braucht es den Container
        var find = Task.Run(() =>
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            Assert.True(ElementSelector.TryParse("name=Strasse;type=button;index=2", out var second, out _));
            var element = ElementAutomation.Find(root, second)!;
            var drucken = ElementAutomation.Find(root, new ElementSelector(null, "🖶", null, [], null, 1))!;
            return (ElementAutomation.VerifiedSelectors(ElementAutomation.Describe(element)!), ElementAutomation.Describe(drucken)!);
        });
        Assert.True(PumpUntil(() => find.IsCompleted, TimeSpan.FromSeconds(30), pause: 1));
        var (selectors, printInfo) = find.Result;
        Assert.Equal("name=Lieferadresse;type=group > name=Strasse;type=button", selectors[0].ToString());
        Assert.Equal("Drucken (Strg+P)", printInfo.HelpText);
        window.Close();
    }

    /// <summary>
    /// Wie &lt;label&gt; in Chrome: Vor dem Feld steht ein Text, der gleich heisst wie das Feld. name= ohne type= meint das
    /// Feld, auch im Container und bei index=. Passt nur ein Text, bleibt er der Treffer.
    /// </summary>
    static void FieldsBeforeTheirLabels()
    {
        System.Windows.Controls.TextBox Field(string name)
        {
            var box = new System.Windows.Controls.TextBox();
            System.Windows.Automation.AutomationProperties.SetName(box, name);
            return box;
        }
        System.Windows.Controls.GroupBox Group(string header, params UIElement[] children)
        {
            var panel = new System.Windows.Controls.StackPanel();
            foreach (var child in children) panel.Children.Add(child);
            return new System.Windows.Controls.GroupBox { Header = header, Content = panel };
        }

        var nachname = Field("Nachname");
        var rechnung = Field("Strasse");
        var liefer = Field("Strasse");
        var window = new Window
        {
            Width = 300, Height = 400, ShowInTaskbar = false,
            Content = new System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new System.Windows.Controls.TextBlock { Text = "Nachname" },
                    nachname,
                    Group("Rechnungsadresse", new System.Windows.Controls.TextBlock { Text = "Strasse" }, rechnung),
                    Group("Lieferadresse", new System.Windows.Controls.TextBlock { Text = "Strasse" }, liefer),
                    new System.Windows.Controls.TextBlock { Text = "Nur Text" },
                },
            },
        };
        Snapshot(window, "labels.png");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        ElementResult Run(string tag)
        {
            Assert.True(KeyTags.TryParse(tag.Trim('{', '}'), null, out var steps, out var error), error);
            var run = Task.Run(() => ElementAutomation.Run((ElementStep)steps[0], hwnd, () => false));
            Assert.True(PumpUntil(() => run.IsCompleted, TimeSpan.FromSeconds(20), pause: 1));
            return run.Result;
        }
        void Sets(string tag, System.Windows.Controls.TextBox expected, string value)
        {
            var result = Run(tag);
            Assert.True(result.Outcome == ElementOutcome.Done, $"{tag}: {result.Outcome} {result.Error}");
            Assert.Equal(value, expected.Text);
        }

        Sets("{set,name=Nachname,\"Muster\"}", nachname, "Muster");
        Sets("{set,name=Lieferadresse > name=Strasse,\"Bahnhofstrasse 1\"}", liefer, "Bahnhofstrasse 1");
        Sets("{set,name=Strasse;index=2,\"Postfach 12\"}", liefer, "Postfach 12");
        Sets("{set,name=Str*,\"Hauptstrasse 3\"}", rechnung, "Hauptstrasse 3");
        // Wer den Text meint, sagt type=, und ein Text allein wird weiterhin gefunden
        var find = Task.Run(() =>
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            Assert.True(ElementSelector.TryParse("name=Nachname;type=text", out var label, out _));
            return (ElementAutomation.Find(root, label)?.Current.ControlType,
                ElementAutomation.Find(root, new ElementSelector(null, "Nur Text", null, [], null, 1))?.Current.ControlType);
        });
        Assert.True(PumpUntil(() => find.IsCompleted, TimeSpan.FromSeconds(20), pause: 1));
        Assert.Equal(System.Windows.Automation.ControlType.Text, find.Result.Item1);
        Assert.Equal(System.Windows.Automation.ControlType.Text, find.Result.Item2);
        window.Close();
    }

    /// <summary>
    /// Nach {rclick,…} wählt {click,name=…} aus dem Kontextmenü: Das Menü ist ein eigenes Fenster, das nicht nach vorne
    /// kommt, und wird vor dem Fenster durchsucht, auch wenn dort ein Button gleich heisst. Ohne echte Mausklicks: das
    /// Fenster steht ausserhalb des Bildschirms, und statt des Kontextmenüs (das ohne aktive App gleich wieder zugeht)
    /// öffnet sich per Code ein Popup, ein Fenster derselben Art.
    /// </summary>
    static void ContextMenuComesFirst()
    {
        var clicked = "";
        var button = new System.Windows.Controls.Button { Name = "kopieren", Content = "Kopieren" };
        button.Click += (_, _) => clicked = "button";
        var item = new System.Windows.Controls.Button { Content = "Kopieren" };
        item.Click += (_, _) => clicked = "menu";
        var list = new System.Windows.Controls.ListBox { Name = "liste", Items = { "Bericht" } };
        var menu = new System.Windows.Controls.Primitives.Popup { Child = item, StaysOpen = true, PlacementTarget = list };
        var window = new Window
        {
            Width = 300, Height = 200, ShowInTaskbar = false,
            Content = new System.Windows.Controls.StackPanel { Children = { button, list } },
        };
        Snapshot(window, "context-menu.png");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        ElementResult Run(string tag)
        {
            Assert.True(KeyTags.TryParse(tag, null, out var steps, out var error), error);
            var run = Task.Run(() => ElementAutomation.Run((ElementStep)steps[0], hwnd, () => false));
            Assert.True(PumpUntil(() => run.IsCompleted, TimeSpan.FromSeconds(15)));
            return run.Result;
        }

        // Doppel- und Rechtsklick gehen immer mit der Maus: ausserhalb des Bildschirms gar nicht erst klicken
        foreach (var tag in new[] { "dblclick,#liste", "rclick,#liste" })
        {
            var offscreen = Run(tag);
            Assert.Equal(ElementOutcome.Failed, offscreen.Outcome);
            Assert.Contains("outside the screen", offscreen.Error);
        }

        // Ohne Menü der Button im Fenster
        Assert.Equal(ElementOutcome.Done, Run("click,name=Kopieren").Outcome);
        Assert.True(PumpUntil(() => clicked == "button", TimeSpan.FromSeconds(5)));

        clicked = "";
        menu.IsOpen = true;
        Assert.True(PumpUntil(() => menu.IsOpen && WindowFinder.Popups(hwnd).Count > 0, TimeSpan.FromSeconds(5)), "Menü als Popup des Fensters");
        Assert.Equal(ElementOutcome.Done, Run("click,name=Kopieren").Outcome);
        Assert.True(PumpUntil(() => clicked == "menu", TimeSpan.FromSeconds(5)), $"geklickt: {clicked}");
        menu.IsOpen = false;
        window.Close();
    }

    /// <summary>
    /// Der Recorder mit echtem UI Automation: Felder betreten und ändern wie ein Mensch, daraus wird der Baustein. Den
    /// Fokus gibt der Test selbst durch, weil sein Fenster nicht vorne sein muss. Klicks (Maus) sind hier nicht dabei.
    /// </summary>
    static void RecorderFollowsFieldsInAForm()
    {
        var name = new System.Windows.Controls.TextBox { Name = "name" };
        var ort = new System.Windows.Controls.TextBox { Name = "ort", Text = "Zürich" };
        var land = new System.Windows.Controls.ComboBox { Name = "land", ItemsSource = new[] { "Deutschland", "Schweiz" }, SelectedIndex = 0 };
        var newsletter = new System.Windows.Controls.CheckBox { Name = "newsletter", Content = "Newsletter" };
        var pw = new System.Windows.Controls.PasswordBox { Name = "pw" };
        var save = new System.Windows.Controls.Button { Name = "speichern", Content = "Speichern" };
        var window = new Window
        {
            Width = 300, Height = 400, ShowInTaskbar = false,
            Content = new System.Windows.Controls.StackPanel { Children = { name, ort, land, newsletter, pw, save } },
        };
        Snapshot(window, "recorder-form.png");
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        // UIA ruft zurück in dieses Fenster: auf einem anderen Thread, der UI-Thread läuft weiter
        T Off<T>(Func<T> work)
        {
            var task = Task.Run(work);
            Assert.True(PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(15)));
            return task.Result;
        }
        System.Windows.Automation.AutomationElement Element(string id) => Off(() =>
            System.Windows.Automation.AutomationElement.FromHandle(hwnd).FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, id)))!;

        // Nur die Fenster dieses Tests, was sonst auf dem Bildschirm passiert, bleibt draussen
        using var recorder = new ActionRecorder(skip: pid => pid != Environment.ProcessId);
        recorder.Start(poll: false);
        void Enter(string id)
        {
            recorder.Focused(Element(id));
            Off(() => { recorder.IdleAsync().Wait(); return true; });
        }

        Enter("name");
        name.Text = "Muster";
        Enter("ort");
        ort.Text = "Bern";
        Enter("land");
        land.SelectedIndex = 1;
        Enter("newsletter");
        newsletter.IsChecked = true;
        Enter("pw");
        pw.Password = "geheim";
        Enter("speichern"); // kein Feld: beendet das Passwortfeld
        var actions = Off(() => recorder.StopAsync().Result);

        Assert.Equal(RecordNote.Password, recorder.Note);
        var lines = Recording.Lines(actions, RecordMode.Mixed);
        Assert.StartsWith("{window,process=", lines[0]);
        Assert.Equal(
            ["{focus,#name}Muster", "{focus,#ort}{clear}Bern", "{set,#land,\"Schweiz\"}", "{set,#newsletter,\"on\"}", "{focus,#pw}"],
            lines.Skip(1));
        Assert.DoesNotContain("geheim", Recording.Build(actions, RecordMode.AlwaysSet));
        window.Close();
    }

    /// <summary>Leiste der Aufnahme (Stand, Tag-Art, nie aktiv) und der Knopf im Editor samt Einfügen auf eigenen Zeilen.</summary>
    static void RecordBarAndEditor()
    {
        UiLanguage.Apply("de");
        var bar = new RecordBar(RecordMode.Mixed);
        Snapshot(bar, "recordbar-empty.png");
        Assert.Contains("0 Schritte", Texts(bar));
        Assert.Contains("(noch nichts)", Texts(bar));
        Assert.Equal(RecordMode.Mixed, bar.Mode);
        Assert.True(Toggle(bar, "Gemischt").IsChecked);

        bar.Show(["{window,process=crm}", "{focus,#ort}{clear}Bern"], RecordNote.Password);
        bar.UpdateLayout();
        Snapshot(bar, "recordbar-password.png");
        Assert.Contains("2 Schritte", Texts(bar));
        Assert.Contains("{focus,#ort}{clear}Bern", Texts(bar));
        Assert.Contains(Texts(bar), t => t.StartsWith("Passwortfeld"));
        bar.Show(["{click,name=OK}"], RecordNote.None);
        bar.UpdateLayout();
        Assert.Contains("1 Schritt", Texts(bar));
        Assert.DoesNotContain(Texts(bar), t => t.StartsWith("Passwortfeld"));

        // Nie aktiv, wie die Leiste des Schritt-für-Schritt-Laufs
        var hwnd = new System.Windows.Interop.WindowInteropHelper(bar).Handle;
        var style = (long)Interop.NativeMethods.GetWindowLongPtr(hwnd, Interop.NativeMethods.GWL_EXSTYLE);
        Assert.NotEqual(0, style & Interop.NativeMethods.WS_EX_NOACTIVATE);
        Assert.False(bar.IsActive);

        // Tag-Art umschalten: genau einer gedrückt, die Änderung wird gemeldet
        var changed = 0;
        bar.ModeChanged += (_, _) => changed++;
        Toggle(bar, "Immer tippen").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(RecordMode.AlwaysType, bar.Mode);
        Assert.Equal(1, changed);
        Assert.Equal([false, false, true], new[] { "Gemischt", "Immer setzen", "Immer tippen" }.Select(t => Toggle(bar, t).IsChecked == true));

        var end = bar.WaitForEndAsync();
        Descendants<System.Windows.Controls.Button>(bar).Single(b => (string)b.Content == "Fertig")
            .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.True(end.IsCompleted && end.Result);
        bar.Close();

        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        using var controller = new AppController(new AppPaths(dir.Path));
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        Snapshot(editor, "editor-record.png");
        Assert.Equal("Aufnehmen", editor.RecordButtonText.Text);

        // Während der Aufnahme gesperrt, der Knopf beendet sie
        editor.LockForRecording(true);
        Assert.True(editor.BodyInput.IsReadOnly);
        Assert.False(editor.TestButton.IsEnabled);
        Assert.False(editor.StepButton.IsEnabled);
        Assert.Equal("Aufnahme beenden", editor.RecordButtonText.Text);
        editor.LockForRecording(false);
        Assert.False(editor.BodyInput.IsReadOnly);
        Assert.True(editor.StepButton.IsEnabled);

        // Mitten in einer Zeile: die Aufnahme beginnt auf der nächsten, der Rest der Zeile folgt danach
        editor.BodyInput.Text = "Hallo{tab}Rest";
        editor.BodyInput.CaretOffset = "Hallo{tab}".Length;
        editor.InsertRecorded("{focus,#ort}Bern\n{click,name=OK}\n");
        Assert.Equal("Hallo{tab}\r\n{focus,#ort}Bern\r\n{click,name=OK}\r\nRest", editor.BodyInput.Text);
        // Am Zeilenanfang ohne zusätzlichen Umbruch
        editor.BodyInput.Text = "";
        editor.InsertRecorded("{click,name=OK}\r\n");
        Assert.Equal("{click,name=OK}\r\n", editor.BodyInput.Text);

        editor.BodyInput.Text = "";
        editor.Close();

        static List<string> Texts(Window window) => Descendants<System.Windows.Controls.TextBlock>(window)
            .Where(t => t.IsVisible).Select(t => t.Text).ToList();

        static System.Windows.Controls.Primitives.ToggleButton Toggle(Window window, string text) =>
            Descendants<System.Windows.Controls.Primitives.ToggleButton>(window).Single(b => (string)b.Content == text);
    }

    /// <summary>
    /// Den Dispatcher laufen lassen, bis <paramref name="done"/> zutrifft oder die Zeit um ist. UIA-Abfragen an eigene
    /// Fenster warten jedes Mal auf diese Schleife: Wer viele macht (Suchen über den ganzen Baum), nimmt eine kurze <paramref name="pause"/>.
    /// </summary>
    static bool PumpUntil(Func<bool> done, TimeSpan timeout, int pause = 20)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(pause);
        }
        return done();
    }

    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null!;
    }

    /// <summary>Die Textvorschau im Popup färbt Tags und Skripte wie der Editor, Hinweise bleiben einfarbig.</summary>
    static void PopupPreviewColors()
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\Formular.txt", "Max{tab}{{ 6 * 7 }}{tab,x}");
        dir.File(@"structure\Links\Intranet.url", "[InternetShortcut]\r\nURL=https://intranet.example.com");
        using var controller = new AppController(new AppPaths(dir.Path));

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "Formular");
        Snapshot(popup, "popup-colors.png");

        var runs = popup.DetailTextBlock.Inlines.OfType<System.Windows.Documents.Run>().ToList();
        Assert.Equal("Max{tab}{{ 6 * 7 }}{tab,x}", string.Concat(runs.Select(r => r.Text)));
        Assert.Same(SnippetHighlighting.BrushFor(SyntaxKind.Key, dark: false), runs.Single(r => r.Text == "{tab}").Foreground);
        Assert.Equal(FontWeights.Bold, runs.First(r => r.Text == "{{").FontWeight);
        Assert.Same(SnippetHighlighting.ErrorUnderline, runs.Single(r => r.Text == "{tab,x}").TextDecorations);
        Assert.Equal(new FontFamily("Cascadia Mono, Consolas"), popup.DetailTextBlock.FontFamily);

        // Dunkel: die hellen Farben aus dem Editor, beim nächsten Zeigen
        UiTheme.Apply(AppTheme.Dark);
        try
        {
            popup.Hide();
            Snapshot(popup, "popup-colors-dark.png");
            var tab = popup.DetailTextBlock.Inlines.OfType<System.Windows.Documents.Run>().First(r => r.Text == "{tab}");
            Assert.Same(SnippetHighlighting.BrushFor(SyntaxKind.Key, dark: true), tab.Foreground);
        }
        finally
        {
            UiTheme.Apply(AppTheme.Light);
        }

        // Ein Weblink zeigt einen Hinweis, ohne Farben
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "Links");
        viewModel.EnterFolder(viewModel.Selected!.Snippet);
        Assert.Equal("Öffnet https://intranet.example.com", popup.DetailTextBlock.Text);
        Assert.Empty(viewModel.DetailSpans);
        popup.Close();

        // Tabs stehen in beiden Vorschauen wie im Editor, Tabstopp alle 4 Zeichen
        dir.File(@"structure\Spalten.txt", "Datum:\tA\r\nZufälliger Satz:\tB");
        controller.Library.Reload();
        viewModel.Reset();
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "Spalten");
        Assert.Equal("Datum:  A\r\nZufälliger Satz:    B".ReplaceLineEndings(), viewModel.DetailText);
        var editor = SnippetEditorWindow.New(controller, Path.Combine(dir.Path, "structure"));
        editor.BodyInput.Text = "{{ 'Datum:\tA' }}";
        editor.BodyInput.Select(3, 0);
        editor.UpdatePreview();
        Assert.Equal("Datum:  A", editor.PreviewText.Text);
        editor.BodyInput.Text = "";
        editor.Close();
    }

    static void Snapshot(Window window, string fileName)
    {
        // Offscreen anzeigen, damit Layout und Ressourcen wirklich aufgelöst werden
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.Show();
        window.UpdateLayout();

        var folder = Environment.GetEnvironmentVariable("QHB_SNAPSHOTS");
        if (string.IsNullOrEmpty(folder)) return;

        // Ein leeres Fenster (Platzhalter für ein anderes Programm) hat keinen Inhalt, dann nur den Hintergrund
        var content = window.Content as FrameworkElement;
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);

        // Hintergrund des Fensters mitzeichnen, der Inhalt selbst ist transparent
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Fluent lässt den Hintergrund für Mica durchsichtig: dann den des Themes, sonst wäre auch das dunkle Theme weiss
            var background = window.Background is SolidColorBrush { Color.A: > 0 } own ? own
                : window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            if (content is not null)
                context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, fileName));
        encoder.Save(stream);
    }

    /// <summary>Ein offenes Menü oder Popup-Inhalt, ohne eigenes Fenster.</summary>
    static void SnapshotElement(FrameworkElement element, string fileName)
    {
        var folder = Environment.GetEnvironmentVariable("QHB_SNAPSHOTS");
        if (string.IsNullOrEmpty(folder)) return;

        element.UpdateLayout();
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = element.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, fileName));
        encoder.Save(stream);
    }

    static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}

[CollectionDefinition(nameof(WindowSmokeTests), DisableParallelization = true)]
public class WindowSmokeTestsCollection;
