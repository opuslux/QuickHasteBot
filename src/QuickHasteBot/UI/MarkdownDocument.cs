using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Md = Markdig.Syntax;
using MdInlines = Markdig.Syntax.Inlines;
using MdTables = Markdig.Extensions.Tables;

namespace QuickHasteBot.UI;

/// <summary>
/// Markdown (das README) als FlowDocument für das Hilfe-Fenster. Nur was ein README so braucht: Überschriften, Absätze,
/// Listen, Tabellen, Code, Zitate, Fett/Kursiv und Links. Farben kommen aus dem Fluent-Theme, wechseln also mit hell/dunkel.
/// </summary>
internal sealed class MarkdownDocument
{
    const string MonoFont = "Cascadia Mono, Consolas";

    // GitHub-Anker: "## Forms: key tags" wird zu #forms-key-tags, wie die Links im Inhaltsverzeichnis
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    readonly Action<string> _openLink;
    readonly Dictionary<string, Block> _anchors = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _links = [];

    public FlowDocument Document { get; }

    /// <summary>Überschriften nach ihrem Anker (ohne #).</summary>
    public IReadOnlyDictionary<string, Block> Anchors => _anchors;

    /// <summary>Alle Link-Ziele, so wie sie im Markdown stehen.</summary>
    public IReadOnlyList<string> Links => _links;

    MarkdownDocument(Action<string> openLink)
    {
        _openLink = openLink;
        Document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            LineHeight = 21,
            PagePadding = new Thickness(28, 20, 28, 28),
            TextAlignment = TextAlignment.Left,
        };
        Document.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        Document.Resources[typeof(Hyperlink)] = LinkStyle();
    }

    /// <summary>Baut das Dokument. <paramref name="openLink"/> bekommt das Ziel eines angeklickten Links.</summary>
    public static MarkdownDocument Build(string markdown, Action<string> openLink)
    {
        var result = new MarkdownDocument(openLink);
        foreach (var block in Markdig.Markdown.Parse(markdown, Pipeline))
            if (result.Convert(block) is { } converted)
                result.Document.Blocks.Add(converted);
        return result;
    }

    // --- Blöcke ---

    Block? Convert(Md.Block block) => block switch
    {
        Md.HeadingBlock heading => Heading(heading),
        Md.ParagraphBlock paragraph => Prose(paragraph.Inline),
        Md.ListBlock list => ListOf(list),
        Md.CodeBlock code => Code(code),
        Md.QuoteBlock quote => Quote(quote),
        MdTables.Table table => TableOf(table),
        Md.ThematicBreakBlock => Rule(),
        Md.LeafBlock leaf => new Paragraph(new Run(leaf.Lines.ToString())) { Margin = new Thickness(0, 0, 0, 10) },
        Md.ContainerBlock container => SectionOf(container),
        _ => null,
    };

    Paragraph Heading(Md.HeadingBlock heading)
    {
        var (size, top) = heading.Level switch
        {
            1 => (28.0, 0.0),
            2 => (21.0, 22.0),
            3 => (16.5, 16.0),
            _ => (14.5, 12.0),
        };
        var paragraph = new Paragraph
        {
            FontSize = size,
            LineHeight = size * 1.3,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, top, 0, heading.Level <= 2 ? 12 : 6),
            KeepWithNext = true,
        };
        // Oberste Ebenen mit einer Linie darunter, wie auf GitHub
        if (heading.Level <= 2)
        {
            paragraph.Padding = new Thickness(0, 0, 0, 6);
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.SetResourceReference(Block.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        }
        AddInlines(paragraph.Inlines, heading.Inline);
        if (heading.GetAttributes().Id is { Length: > 0 } id)
            _anchors.TryAdd(id, paragraph);
        return paragraph;
    }

    Paragraph Prose(MdInlines.ContainerInline? inlines)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        AddInlines(paragraph.Inlines, inlines);
        return paragraph;
    }

    List ListOf(Md.ListBlock block)
    {
        var list = new List
        {
            MarkerStyle = block.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(22, 0, 0, 0),
        };
        if (block.IsOrdered && int.TryParse(block.OrderedStart, out var start))
            list.StartIndex = start;

        foreach (var child in block.OfType<Md.ListItemBlock>())
        {
            var item = new ListItem();
            foreach (var inner in child)
            {
                if (Convert(inner) is not { } converted) continue;
                // In der Liste enger als im Fliesstext
                converted.Margin = new Thickness(0, 0, 0, 4);
                item.Blocks.Add(converted);
            }
            list.ListItems.Add(item);
        }
        return list;
    }

    static Paragraph Code(Md.CodeBlock block)
    {
        var paragraph = new Paragraph
        {
            FontFamily = new FontFamily(MonoFont),
            FontSize = 12.5,
            LineHeight = 18,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
        };
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        var lines = block.Lines.ToString().ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i]));
        }
        return paragraph;
    }

    Section Quote(Md.QuoteBlock quote)
    {
        var section = SectionOf(quote);
        section.Padding = new Thickness(12, 0, 0, 0);
        section.BorderThickness = new Thickness(3, 0, 0, 0);
        section.Margin = new Thickness(0, 0, 0, 10);
        section.SetResourceReference(Block.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        section.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
        return section;
    }

    Section SectionOf(Md.ContainerBlock container)
    {
        var section = new Section();
        foreach (var child in container)
            if (Convert(child) is { } converted)
                section.Blocks.Add(converted);
        return section;
    }

    static Paragraph Rule()
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 6, 0, 16), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 1, LineHeight = 1 };
        paragraph.SetResourceReference(Block.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        return paragraph;
    }

    Table TableOf(MdTables.Table block)
    {
        var rows = block.OfType<MdTables.TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };

        // WPF kennt keine automatische Spaltenbreite: anteilig nach dem längsten Inhalt, damit "Key | Action" nicht halbiert wird
        for (var c = 0; c < columns; c++)
        {
            var longest = rows.Where(r => c < r.Count).Max(r => CellText((MdTables.TableCell)r[c]).Length);
            table.Columns.Add(new TableColumn { Width = new GridLength(Math.Clamp(longest, 8, 60), GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            if (row.IsHeader)
            {
                tableRow.FontWeight = FontWeights.SemiBold;
                tableRow.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            }
            foreach (var cell in row.OfType<MdTables.TableCell>())
            {
                var tableCell = new TableCell { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                tableCell.SetResourceReference(TableCell.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                foreach (var inner in cell)
                {
                    if (Convert(inner) is not { } converted) continue;
                    converted.Margin = new Thickness(0);
                    tableCell.Blocks.Add(converted);
                }
                tableRow.Cells.Add(tableCell);
            }
            group.Rows.Add(tableRow);
        }
        table.RowGroups.Add(group);
        return table;
    }

    static string CellText(MdTables.TableCell cell) =>
        string.Concat(Md.MarkdownObjectExtensions.Descendants<MdInlines.LiteralInline>(cell).Select(l => l.Content.ToString())) +
        string.Concat(Md.MarkdownObjectExtensions.Descendants<MdInlines.CodeInline>(cell).Select(c => c.Content));

    // --- Inlines ---

    void AddInlines(InlineCollection target, MdInlines.ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
            if (Convert(inline) is { } converted)
                target.Add(converted);
    }

    Inline? Convert(MdInlines.Inline inline)
    {
        switch (inline)
        {
            case MdInlines.LiteralInline literal:
                return new Run(literal.Content.ToString());
            case MdInlines.CodeInline code:
                var run = new Run(code.Content) { FontFamily = new FontFamily(MonoFont), FontSize = 12.5 };
                run.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                return run;
            case MdInlines.LineBreakInline lineBreak:
                return lineBreak.IsHard ? new LineBreak() : new Run(" ");
            case MdInlines.HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());
            case MdInlines.HtmlInline html:
                return new Run(html.Tag);
            case MdInlines.AutolinkInline autolink:
                return Link(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, new Run(autolink.Url));
            case MdInlines.LinkInline { IsImage: true } image:
                // Bilder gibt es im README nicht, sonst wenigstens der Alternativtext
                var alt = new Span();
                AddInlines(alt.Inlines, image);
                return alt;
            case MdInlines.LinkInline link:
                var hyperlink = Link(link.GetDynamicUrl?.Invoke() ?? link.Url ?? "", null);
                AddInlines(hyperlink.Inlines, link);
                return hyperlink;
            case MdInlines.EmphasisInline emphasis:
                Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                AddInlines(span.Inlines, emphasis);
                return span;
            case MdInlines.ContainerInline container:
                var group = new Span();
                AddInlines(group.Inlines, container);
                return group;
            default:
                return null;
        }
    }

    Hyperlink Link(string url, Inline? content)
    {
        _links.Add(url);
        var hyperlink = content is null ? new Hyperlink() : new Hyperlink(content);
        if (!url.StartsWith('#'))
            hyperlink.ToolTip = url;
        hyperlink.Click += (_, _) => _openLink(url);
        return hyperlink;
    }

    /// <summary>In der Akzentfarbe, unterstrichen nur unter der Maus wie die übrigen Links im Programm.</summary>
    static Style LinkStyle()
    {
        var style = new Style(typeof(Hyperlink));
        style.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("AccentTextFillColorPrimaryBrush")));
        style.Setters.Add(new Setter(Inline.TextDecorationsProperty, null));
        style.Setters.Add(new Setter(FrameworkContentElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        var hover = new Trigger { Property = ContentElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Inline.TextDecorationsProperty, TextDecorations.Underline));
        style.Triggers.Add(hover);
        return style;
    }
}
