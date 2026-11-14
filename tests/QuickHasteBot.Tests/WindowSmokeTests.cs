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
public partial class WindowSmokeTests
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
