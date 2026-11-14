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

/// <summary>Elemente in echten Fenstern: Picker, Selektoren, Kontextmenüs und Aufnahme.</summary>
public partial class WindowSmokeTests
{
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
}
