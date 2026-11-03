using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickHasteBot.Core;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>
/// Lädt die Fenster offscreen mit Fluent-Theme: findet kaputtes XAML, Bindings und fehlende Theme-Ressourcen.
/// Mit QHB_SNAPSHOTS=&lt;Ordner&gt; werden die Fenster zusätzlich als PNG gespeichert.
/// </summary>
public class WindowSmokeTests
{
    [Fact]
    public void Popup_and_settings_render_with_real_structure() => RunOnSta(() =>
    {
        using var dir = new TempDirectory();
        dir.File(@"structure\1-Temp\00-Temp.txt", "temp");
        dir.File(@"structure\4-Grüsse\01-MfG-DE.txt", "Freundliche Grüsse\r\nMax Muster");
        dir.File(@"structure\4-Grüsse\03-MfG-EN.txt", "Kind regards\r\nMax Muster");
        dir.File(@"structure\4-Grüsse\Intern\01-Gruss.txt", "Gruss");
        dir.File(@"structure\6-Weblinks\www.google.com.url", "[InternetShortcut]\r\nURL=https://www.google.com");
        dir.File(@"structure\ascii-table.txt", "!\"#$%&'()*+,-./");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.Light };
        app.Resources.Add("BoolToVisibility", new System.Windows.Controls.BooleanToVisibilityConverter());
        try
        {
            using var controller = new AppController(new AppPaths(dir.Path));

            var popup = new SearchPopup(controller);
            var viewModel = (SearchPopupViewModel)popup.DataContext;
            viewModel.Reset();
            Assert.Equal(["1-Temp", "4-Grüsse", "6-Weblinks", "ascii-table"], viewModel.Items.Select(i => i.Snippet.Name));
            Snapshot(popup, "popup-browse.png");

            viewModel.Query = "mfg";
            Assert.Equal(2, viewModel.Items.Count);
            Assert.Contains("Freundliche Grüsse", viewModel.Preview);
            Snapshot(popup, "popup-search.png");

            viewModel.Query = "";
            viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "6-Weblinks").Snippet);
            Assert.Equal(["Textbausteine", "6-Weblinks"], viewModel.Breadcrumbs.Select(c => c.Name));
            Assert.Equal(["", null], viewModel.Breadcrumbs.Select(c => c.Target));
            Assert.Equal("Öffnet https://www.google.com", viewModel.Preview);
            Assert.True(viewModel.GoUp());
            Assert.Equal("6-Weblinks", viewModel.Selected?.Snippet.Name);

            // Über die Kopfzeile zurück, der Ordner auf dem Weg bleibt ausgewählt
            viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "4-Grüsse").Snippet);
            viewModel.EnterFolder(viewModel.Items.Single(i => i.Snippet.Name == "Intern").Snippet);
            Assert.Equal(["Textbausteine", "4-Grüsse", "Intern"], viewModel.Breadcrumbs.Select(c => c.Name));
            Snapshot(popup, "popup-breadcrumbs.png");
            viewModel.GoTo(viewModel.Breadcrumbs[0].Target!);
            Assert.Equal("4-Grüsse", viewModel.Selected?.Snippet.Name);

            var settings = new SettingsWindow(controller);
            Snapshot(settings, "settings.png");
            settings.Close();
            popup.Close();
        }
        finally
        {
            app.Shutdown();
        }
    });

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
