using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace QuickHasteBot.UI;

/// <summary>
/// Das README, das in die exe eingebettet ist, als Hilfe. Links: #anker springen im Dokument, relative Pfade
/// (LICENSE, .github/…) öffnen die Datei auf GitHub, http(s) und mailto im Browser bzw. Mailprogramm.
/// </summary>
internal partial class HelpWindow : Window
{
    /// <summary>Abschnitt "## Editor" im README, für F1 im Editor.</summary>
    public const string EditorAnchor = "editor";

    const string RepositoryFiles = "https://github.com/opuslux/QuickHasteBot/blob/main/";

    readonly Action<string> _openExternal;

    internal MarkdownDocument Markdown { get; }

    public HelpWindow(Action<string> openExternal)
    {
        _openExternal = openExternal;
        InitializeComponent();
        Markdown = MarkdownDocument.Build(ReadReadme(), OnLink);
        Viewer.Document = Markdown.Document;
    }

    /// <summary>Das README aus der Ressource (siehe QuickHasteBot.csproj).</summary>
    internal static string ReadReadme()
    {
        using var stream = typeof(HelpWindow).Assembly.GetManifestResourceStream("README.md")
            ?? throw new InvalidOperationException("README.md fehlt in den Ressourcen");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Wohin ein Link aus dem README führt: null für einen Anker im Dokument oder ein unbekanntes Schema.</summary>
    internal static string? ExternalTarget(string url)
    {
        if (url.Length == 0 || url.StartsWith('#')) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" or "mailto" ? absolute.AbsoluteUri : null;
        // Relativ zum README, also zum Repository
        while (url.StartsWith("./", StringComparison.Ordinal))
            url = url[2..];
        return RepositoryFiles + url.TrimStart('/');
    }

    void OnLink(string url)
    {
        if (url.StartsWith('#'))
            ScrollTo(url[1..]);
        else if (ExternalTarget(url) is { } target)
            _openExternal(target);
    }

    /// <summary>Springt zur Überschrift mit diesem Anker, die Überschrift oben im Fenster. False, wenn es sie nicht gibt.</summary>
    public bool ScrollTo(string anchor)
    {
        if (!Markdown.Anchors.TryGetValue(anchor, out var block)) return false;
        if (!IsLoaded)
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ScrollTo(anchor));
            return true;
        }

        // Erst ans Ende, dann zurück: BringIntoView von unten her lässt die Überschrift oben stehen statt am unteren Rand
        if (Scroller() is { } scroller)
        {
            scroller.ScrollToEnd();
            scroller.UpdateLayout();
        }
        block.BringIntoView();
        return true;
    }

    internal ScrollViewer? Scroller()
    {
        Viewer.ApplyTemplate();
        return Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer;
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
