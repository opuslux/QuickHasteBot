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
    /// <summary>Hotkeys aus snippets.json: Badge im Popup, Konflikte im Dialog und in den Einstellungen, verwaiste grau.</summary>
    static void FileHotkeys()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        dir.File(@"structure\3-SQL\Audit.txt", "insert");
        dir.File(@"structure\Gruss.txt", "Gruss");
        dir.File(@"structure\Gutschein.txt", "Gutschein");
        dir.File(@"structure\Popup.txt", "Popup");
        dir.File(@"structure\Notiz.txt", "Notiz");
        // Ein altes Tag im Namen gilt nicht mehr
        dir.File(@"structure\Alt [Ctrl+Alt+L].txt", "Alt");
        dir.File("snippets.json", """
            {
              "Entries": {
                "3-SQL": { "Hotkey": "Ctrl+Alt+S" },
                "3-SQL\\Audit.txt": { "Hotkey": "Ctrl+Alt+A" },
                "Gruss.txt": { "Hotkey": "Ctrl+Alt+G" },
                "Gutschein.txt": { "Hotkey": "Ctrl+Alt+G" },
                "Popup.txt": { "Hotkey": "Ctrl+Shift+Y" },
                "Weg\\Umbenannt.txt": { "Hotkey": "Ctrl+Alt+W" }
              }
            }
            """);
        using var controller = new AppController(new AppPaths(dir.Path));
        controller.FileHotkeys.Sync(announce: false);

        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        Assert.Equal(["3-SQL", "Alt [Ctrl+Alt+L]", "Gruss", "Gutschein", "Notiz", "Popup"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal(["Ctrl+Alt+S", null, "Ctrl+Alt+G", "Ctrl+Alt+G", null, "Ctrl+Shift+Y"], viewModel.Items.Select(i => i.HotkeyText));
        Assert.Equal([false, false, false, true, false, true], viewModel.Items.Select(i => i.HasHotkeyProblem));
        Assert.Equal("Schon vergeben an «Gruss».", viewModel.Items[3].HotkeyTooltip);
        Assert.Equal("Hotkey Ctrl+Alt+S, gilt in jedem Programm", viewModel.Items[0].HotkeyTooltip);
        Snapshot(popup, "popup-hotkeys.png");

        // Hotkey eines Ordners: das Popup beginnt dort
        viewModel.Reset(controller.Library.All.Single(s => s.Name == "3-SQL").RelativePath);
        Assert.Equal(["Bausteine", "3-SQL"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["Audit"], viewModel.Items.Select(i => i.Snippet.Name));
        popup.Close();

        // Dialog: vergebene Kombinationen sperren, eine freie lässt sich übernehmen
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
        Snapshot(dialog, "hotkey-dialog.png");
        dialog.Close();

        // Übernehmen: steht in snippets.json, der Dateiname bleibt
        controller.SetFileHotkey(notiz.RelativePath, new HotkeyGesture(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, Key.N));
        Assert.True(File.Exists(notiz.FullPath));
        Assert.Equal("Ctrl+Alt+N", controller.Library.All.Single(s => s.Name == "Notiz").Hotkey.ToString());
        Assert.Contains("\"Notiz.txt\"", File.ReadAllText(dir.Path + @"\snippets.json"));

        // Umbenennen im Programm: der Hotkey zieht mit
        var renamed = SnippetFiles.Rename(notiz.FullPath, "Notizen.txt");
        controller.SnippetMoved(notiz.FullPath, renamed);
        controller.Library.Reload();
        Assert.Equal("Ctrl+Alt+N", controller.Library.All.Single(s => s.Name == "Notizen").Hotkey.ToString());

        // Neuer Ordner: der Hinweis nennt den offenen Ordner, vergebene Namen sperren
        var sql = controller.Library.All.Single(s => s.Name == "3-SQL");
        var newFolder = NameDialog.ForNewFolder(sql.FullPath, "Bausteine › 3-SQL");
        Assert.Equal("Neuer Ordner", newFolder.FileName);
        Assert.Equal("Neue Ordner entstehen immer im Ordner, der im Popup gerade offen ist: Bausteine › 3-SQL", newFolder.Hint.Text);
        Assert.Equal(Visibility.Visible, newFolder.Hint.Visibility);
        Assert.True(newFolder.ConfirmButton.IsEnabled);
        Snapshot(newFolder, "new-folder.png");
        newFolder.NameInput.Text = "Audit.txt";
        Assert.Equal(Strings.NameExists, newFolder.Problem.Text);
        Assert.False(newFolder.ConfirmButton.IsEnabled);
        newFolder.Close();

        // Einstellungen: alle Hotkeys, die zwei Konflikte rot, der verwaiste grau am Ende
        controller.FileHotkeys.Sync(announce: false);
        var settings = new SettingsWindow(controller);
        settings.ShowFileHotkeys();
        var rows = settings.FileHotkeysList.Items.Cast<SettingsWindow.FileHotkeyRow>().ToList();
        Assert.Equal(["Ctrl+Alt+S", "Ctrl+Alt+A", "Ctrl+Alt+G", "Ctrl+Alt+G", "Ctrl+Alt+N", "Ctrl+Shift+Y", "Ctrl+Alt+W"],
            rows.Select(r => r.Hotkey));
        Assert.Equal(2, rows.Count(r => r.IsProblem));
        Assert.Equal([false, false, false, false, false, false, true], rows.Select(r => r.IsOrphan));
        Assert.Equal(["Umbenannt.txt", "Weg", "Eintrag nicht gefunden"], new[] { rows[^1].Name, rows[^1].Location, rows[^1].Status });
        Assert.Equal("4 aktiv, 2 wirken nicht, 1 ohne Eintrag (umbenannt oder verschoben, während QuickHasteBot nicht lief?)",
            settings.FileHotkeysSummary.Text);
        Snapshot(settings, "settings-hotkeys.png");

        // Entfernen (Entf) nimmt den verwaisten aus snippets.json
        settings.FileHotkeysList.SelectedIndex = rows.Count - 1;
        settings.FileHotkeysList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings)!, 0, Key.Delete)
            { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.Equal(6, settings.FileHotkeysList.Items.Count);
        Assert.DoesNotContain("Umbenannt", File.ReadAllText(dir.Path + @"\snippets.json"));
        settings.Close();
    }

    /// <summary>Tooltip am Icon und Tab "Statistik": Zähler nach Quelle, gelernte Suchtexte, sortieren, filtern, vergessen.</summary>
    static void UsageStatistics()
    {
        using var dir = new TempDirectory();
        dir.File("settings.json", """{ "Language": "de" }""");
        dir.File(@"structure\1-Grüsse\MfG.txt", "Mit freundlichen Grüssen");
        dir.File(@"structure\1-Grüsse\Gruss.txt", "Gruss");
        dir.File(@"structure\Notiz.txt", "Notiz");
        using var controller = new AppController(new AppPaths(dir.Path));
        var mfg = controller.Library.All.Single(s => s.Name == "MfG");
        controller.RecordUse(mfg, UseSource.Search, "mf");
        controller.RecordUse(mfg, UseSource.Search, "MF");
        controller.RecordUse(mfg, UseSource.Hotkey);
        controller.RecordUse(controller.Library.All.Single(s => s.Name == "Gruss"), UseSource.Browse);

        // Tooltip am Icon: bei der Suche auch, warum der Eintrag oben steht; nie Benutztes hat keinen
        var popup = new SearchPopup(controller);
        var viewModel = (SearchPopupViewModel)popup.DataContext;
        viewModel.Reset();
        viewModel.Query = "mf";
        Assert.Equal("""
            3× benutzt (Suche 2 · Blättern 0 · Hotkey 1)
            Zuletzt heute
            Gelernt: „mf“ (2×)
            Weiter oben, weil nach „mf“ 2× gewählt
            """.ReplaceLineEndings(), popup.UsageTooltip(viewModel.Items.Single(i => i.Snippet.Name == "MfG")));
        viewModel.Query = "";
        Assert.Null(popup.UsageTooltip(viewModel.Items.Single(i => i.Snippet.Name == "Notiz")));
        popup.Close();

        // Tab: die meistbenutzten zuerst, unbenutzte Ordner fehlen
        var settings = new SettingsWindow(controller);
        settings.StatsTab.IsSelected = true;
        List<string> Names() => settings.StatsList.Items.Cast<SettingsWindow.UsageRow>().Select(r => r.Name).ToList();
        var rows = settings.StatsList.Items.Cast<SettingsWindow.UsageRow>().ToList();
        Assert.Equal(["MfG", "Gruss", "Notiz"], Names());
        Assert.Equal(["3", "2", "", "1", "heute", "„mf“ (2×)"],
            new[] { rows[0].TotalText, rows[0].SearchText, rows[0].BrowseText, rows[0].HotkeyText, rows[0].LastText, rows[0].LearnedText });
        Assert.Equal(["0", "nie"], new[] { rows[2].TotalText, rows[2].LastText });
        Assert.Equal($"Seit {UsageText.Date(controller.SnippetData.Since)} · 4× benutzt · 2 von 3 Bausteinen",
            settings.StatsSummary.Text);
        var headers = settings.StatsHeader.Children.OfType<System.Windows.Controls.TextBlock>().ToList();
        Assert.Equal("Gesamt ▾", headers[2].Text);
        Snapshot(settings, "settings-stats.png");

        // Spaltenkopf sortiert, ein zweiter Klick dreht um
        void ClickHeader(int column) => headers[column].RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        ClickHeader(0);
        Assert.Equal(["Gruss", "MfG", "Notiz"], Names());
        Assert.Equal("Name ▴", headers[0].Text);
        ClickHeader(0);
        Assert.Equal(["Notiz", "MfG", "Gruss"], Names());

        // Nur nie benutzte, Suchfeld (auch im Ordner)
        settings.StatsNeverUsedInput.IsChecked = true;
        settings.StatsNeverUsedInput.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(["Notiz"], Names());
        settings.StatsNeverUsedInput.IsChecked = false;
        settings.StatsNeverUsedInput.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        settings.StatsFilterInput.Text = "grüsse";
        Assert.Equal(["MfG", "Gruss"], Names());
        settings.StatsFilterInput.Text = "";

        // Gelernten Suchtext vergessen: weg aus Liste und snippets.json, die Zähler bleiben
        var row = settings.StatsList.Items.Cast<SettingsWindow.UsageRow>().Single(r => r.Name == "MfG");
        settings.ForgetQuery(row, row.Stats!.Queries[0]);
        row = settings.StatsList.Items.Cast<SettingsWindow.UsageRow>().Single(r => r.Name == "MfG");
        Assert.Equal(("3", ""), (row.TotalText, row.LearnedText));
        controller.SnippetData.Flush();
        Assert.DoesNotContain("\"mf\"", File.ReadAllText(dir.Path + @"\snippets.json"));
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
