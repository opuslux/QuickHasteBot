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

/// <summary>Popup: Datei-Hotkeys, Höhe, offen lassen, Vorschaufarben, ask()-Fenster und Update-Link.</summary>
public partial class WindowSmokeTests
{
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
        controller.FileHotkeys.Sync(announce: false);

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
        var rename = NameDialog.ForRename(gruss, name => controller.FileHotkeys.ConflictForName(name, false, gruss.FullPath, gruss.Hotkey));
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
            name => controller.FileHotkeys.ConflictForName(name, true, null, null));
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
}
