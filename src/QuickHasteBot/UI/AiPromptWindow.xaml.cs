using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.UI;

/// <summary>
/// Fragt, was ein Skriptblock tun soll, und schickt den Prompt an den gewählten KI-Dienst. Bleibt nach dem Öffnen
/// stehen, damit man den Prompt anpassen und erneut schicken kann. Eigenes Fenster in der Taskleiste, nicht am
/// Popup: das liegt immer im Vordergrund und würde den Browser verdecken.
/// Unten lässt sich die Antwort einfügen und gleich ausführen.
/// Enter macht eine neue Zeile, Strg+Enter öffnet den Browser, Esc schliesst. Der Dienst wird sofort gespeichert,
/// der Text beim Senden und Schliessen, damit er auch nach einem Neustart wieder dasteht.
/// </summary>
internal partial class AiPromptWindow : Window
{
    // Erst ausführen, wenn das Tippen ruht: Eine Endlosschleife hält sonst bei jedem Zeichen 250 ms an
    static readonly TimeSpan TestDelay = TimeSpan.FromMilliseconds(300);

    readonly AppController _app;
    readonly DispatcherTimer _testTimer;
    readonly bool _loading;
    string? _template;

    public AiPromptWindow(AppController app)
    {
        _loading = true;
        InitializeComponent();
        _app = app;
        _testTimer = new DispatcherTimer(TestDelay, DispatcherPriority.Background, (_, _) => TestAnswer(), Dispatcher) { IsEnabled = false };
        (ServiceInput.ItemsSource, ServiceInput.SelectedItem) = app.AiServiceChoices();
        UpdateOpenButton();
        RequirementInput.Text = app.Settings.AiPromptText;
        OnRequirementChanged(this, null);
        RefreshTemplate();
        _loading = false;

        // Nach dem Bearbeiten von ai-prompt.txt zurück im Fenster: Vorschau mit der geänderten Vorlage
        Activated += (_, _) => RefreshTemplate();
        // Auch beim Beenden der App, dabei gehen alle Fenster zu
        Closed += (_, _) =>
        {
            _testTimer.Stop();
            _app.SaveSettings();
        };
        Loaded += (_, _) =>
        {
            RequirementInput.Focus();
            RequirementInput.SelectAll();
        };
    }

    public string Requirement => RequirementInput.Text.Trim();

    void OnRequirementChanged(object sender, TextChangedEventArgs? e)
    {
        if (!_loading)
            _app.Settings.AiPromptText = RequirementInput.Text;
        Placeholder.Visibility = RequirementInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
        OpenButton.IsEnabled = Requirement.Length > 0;
    }

    void UpdatePreview() => PromptPreview.Text = ScriptPrompt.Build(Requirement, _template);

    void OnServiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ServiceInput.SelectedItem is not AiService service) return;
        _app.ChangeAiService(service);
        UpdateOpenButton();
        RequirementInput.Focus();
    }

    // "In ChatGPT öffnen" usw., bei einer selbst eingetragenen Adresse "Im Browser öffnen"
    void UpdateOpenButton() =>
        OpenButton.Content = ServiceInput.SelectedItem is AiService service && ScriptPrompt.ServiceFor(service.Url) is not null
            ? string.Format(Strings.AiPromptOpenFormat, service.Name)
            : Strings.AiPromptOpen;

    void OnEditTemplate(object sender, MouseButtonEventArgs e)
    {
        _app.EditAiPromptTemplate();
        RefreshTemplate();
    }

    void OnResetTemplate(object sender, MouseButtonEventArgs e)
    {
        if (MessageBox.Show(this, Strings.AiPromptResetConfirm, "QuickHasteBot", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;

        _app.ResetAiPromptTemplate(new WindowInteropHelper(this).Handle);
        RefreshTemplate();
    }

    /// <summary>Vorlage neu lesen: Vorschau aktualisieren, "Zurücksetzen" nur zeigen, wenn es eine eigene gibt.</summary>
    internal void RefreshTemplate()
    {
        _template = _app.ReadAiPromptTemplate();
        ResetTemplateLink.Visibility = _app.HasAiPromptTemplate ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    void OnAnswerChanged(object sender, TextChangedEventArgs e)
    {
        AnswerPlaceholder.Visibility = AnswerInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _testTimer.Stop();
        _testTimer.Start();
    }

    /// <summary>
    /// Führt die eingefügte Antwort aus wie beim Einfügen eines Bausteins und zeigt Ergebnis oder Fehler.
    /// Läuft sie, lässt sie sich als fertiger Block kopieren.
    /// </summary>
    internal void TestAnswer()
    {
        _testTimer.Stop();
        CopyRow.Visibility = Visibility.Collapsed;
        Block = ScriptPrompt.ToBlock(AnswerInput.Text, out var wrapped);
        if (Block.Length == 0)
        {
            TestResultBox.Visibility = Visibility.Collapsed;
            return;
        }

        var clipboard = SnippetScript.UsesClipboard(Block) ? PasteService.TryGetClipboardText() : "";
        try
        {
            var result = SnippetScript.Render(Block, new ScriptContext(DateTimeOffset.Now, CultureInfo.CurrentCulture, clipboard));
            if (result.Length == 0)
                ShowTestResult(Strings.AiPromptTestEmpty, "TextFillColorSecondaryBrush", monospace: false);
            else
                ShowTestResult(result, "TextFillColorPrimaryBrush", monospace: true);
            WrappedHint.Visibility = wrapped ? Visibility.Visible : Visibility.Hidden;
            CopyRow.Visibility = Visibility.Visible;
        }
        catch (SnippetScriptException ex)
        {
            ShowTestResult(string.Format(Strings.AiPromptTestErrorFormat, ex.Block, ex.Message), "SystemFillColorCriticalBrush", monospace: false);
        }
    }

    /// <summary>Was "Block kopieren" in die Zwischenablage legt: die Antwort ohne Codeblock-Zäune, mit {{ }}.</summary>
    internal string Block { get; private set; } = "";

    void OnCopyBlock(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Block);
        }
        catch (ExternalException ex)
        {
            Log.Error("Block konnte nicht in die Zwischenablage gelegt werden", ex);
        }
    }

    void ShowTestResult(string text, string brushKey, bool monospace)
    {
        TestResult.Text = text;
        TestResult.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        TestResult.FontFamily = monospace ? new FontFamily("Cascadia Mono, Consolas") : FontFamily;
        TestResultBox.Visibility = Visibility.Visible;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (OpenButton.IsEnabled)
                OnOpen(this, e);
            e.Handled = true;
        }
    }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        _app.SaveSettings();
        _app.OpenAiPrompt(Requirement);
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();
}
