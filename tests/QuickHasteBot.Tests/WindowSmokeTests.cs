using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>
/// Lädt die Fenster offscreen mit Fluent-Theme: findet kaputtes XAML, Bindings und fehlende Theme-Ressourcen.
/// Mit QHB_SNAPSHOTS=&lt;Ordner&gt; werden die Fenster zusätzlich als PNG gespeichert.
/// </summary>
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
            German();
            English();
            SwitchLanguage();
        }
        finally
        {
            app.Shutdown();
        }
    });

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
        Assert.Equal(["Textbausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["01-Gruss"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Gruss", viewModel.DetailText);
        viewModel.GoTo("");

        // Ein Buchstabe sucht noch nicht, der Ordner bleibt stehen
        viewModel.Query = "m";
        Assert.False(viewModel.IsSearching);
        Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
        Assert.Equal("Textbausteine", viewModel.Breadcrumbs[0].Name);

        viewModel.Query = "mfg";
        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.ShowDetailItems);
        Assert.True(viewModel.DetailIsText);
        Assert.StartsWith("Freundliche Grüsse" + Environment.NewLine + "Max Muster" + Environment.NewLine, viewModel.DetailText);
        Snapshot(popup, "popup-search.png");

        // Aus der Suche heraus führt ein Ordner von rechts in den Blätter-Modus
        viewModel.Query = "grüsse";
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse");
        viewModel.EnterFolder(viewModel.DetailItems.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal("", viewModel.Query);
        Assert.Equal(["Textbausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        viewModel.GoTo("");

        viewModel.Query = "";
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "6-Weblinks").Snippet);
        Assert.Equal(["Textbausteine", "6-Weblinks"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal(["", null], viewModel.Breadcrumbs.Select(c => c.Target));
        Assert.Equal("Öffnet https://www.google.com", viewModel.DetailText);
        Assert.False(viewModel.DetailIsText);
        Assert.True(viewModel.GoUp());
        Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

        // Über die Kopfzeile zurück, der Ordner auf dem Weg bleibt ausgewählt
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "Intern").Snippet);
        Assert.Equal(["Textbausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
        Snapshot(popup, "popup-breadcrumbs.png");
        viewModel.GoTo(viewModel.Breadcrumbs[0].Target!);
        Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);

        // Mit der Stecknadel beginnt das nächste Öffnen im Ordner und beim Eintrag vom Schliessen
        viewModel.RememberFolder = true;
        viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
        viewModel.Selected = viewModel.Items.Single(i => i.Snippet.Name == "03-MfG-EN");
        popup.Hide();
        Assert.Contains(@"""LastFolder"": ""4-Grüsse""", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Reset();
        Assert.Equal(["Textbausteine", "4-Grüsse"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.Equal("03-MfG-EN", viewModel.Selected?.Snippet.Name);

        // Ohne Stecknadel geht es wieder oben los, ein verschwundener Ordner ebenso
        viewModel.RememberFolder = false;
        viewModel.Reset();
        Assert.Equal(["Textbausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        controller.Settings.RememberFolder = true;
        controller.Settings.LastFolder = "Gibt-es-nicht";
        viewModel.Reset();
        Assert.Equal(["Textbausteine"], viewModel.Breadcrumbs.Select(c => c.Name));
        Assert.True(viewModel.RememberFolder);
        controller.Settings.RememberFolder = false;

        // Umbruch der Textvorschau wird sofort gespeichert
        Assert.False(viewModel.WrapPreview);
        viewModel.WrapPreview = true;
        Assert.Contains(@"""WrapPreview"": true", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
        viewModel.Query = "mfg";
        Snapshot(popup, "popup-wrap.png");
        viewModel.WrapPreview = false;
        viewModel.Query = "";

        var settings = new SettingsWindow(controller);
        Assert.Equal(PopupPosition.TopRight, settings.PositionInput.Position);
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
        // Die gespeicherte Aufteilung der Spalten gilt
        Assert.Equal(popup.ListColumn.Width, popup.DetailColumn.Width);

        // Zu schmal gespeichert: so breit wie die Zeile mit den Tastenkürzeln, sonst die gespeicherte Breite
        popup.ApplyWidth();
        Assert.Equal(popup.MinWidth, popup.Width);
        Snapshot(popup, "popup-narrow.png");
        Assert.InRange(popup.MinWidth, 300, 600);
        // Die ganze Zeile hat Platz, auch "Ctrl+, Settings" am Ende
        Assert.True(popup.HintBar.ActualWidth >= popup.HintBar.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth + c.Margin.Left + c.Margin.Right) - 0.5);
        controller.Settings.PopupWidth = 900;
        popup.ApplyWidth();
        Assert.Equal(900, popup.Width);
        Snapshot(popup, "popup-en.png");

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

        var settings = new SettingsWindow(controller);
        Assert.Equal("QuickHasteBot – Settings", settings.Title);
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

        controller.ShowSettings(new Point(-20000, -20000));
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);

        controller.ChangeLanguage("en");
        Assert.Equal("QuickHasteBot – Settings", Current().Title);
        Assert.Equal(-20000, Current().Left);
        Assert.Equal("Snippets", Strings.BreadcrumbRoot);
        Assert.Equal("en", controller.Settings.Language);

        controller.ChangeLanguage("de");
        Assert.Equal("QuickHasteBot – Einstellungen", Current().Title);
        Current().Close();
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

        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);

        // Hintergrund des Fensters mitzeichnen, der Inhalt selbst ist transparent
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
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
