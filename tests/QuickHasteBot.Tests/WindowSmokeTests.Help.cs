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

/// <summary>Hilfe: README mit Ankern, Markdown-Blöcke und Links.</summary>
public partial class WindowSmokeTests
{
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
}
