using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Formatierter Text in {html}…{/html}: aus der Zwischenablage aufräumen, als Klartext lesen, Text als HTML schreiben,
/// und das Format "HTML Format" (CF_HTML), in dem Windows formatierten Text in der Zwischenablage erwartet.
/// </summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<!--\s*StartFragment\s*-->(.*)<!--\s*EndFragment\s*-->", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Fragment();

    [GeneratedRegex(@"<body[^>]*>(.*?)(?:</body>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Body();

    // Was Office nur für sich selbst schreibt: bedingte Kommentare (VML-Bilder für Word), <o:p> und andere Tags mit Präfix
    [GeneratedRegex(@"<!--\[if.*?<!\[endif\]-->", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HiddenConditional();

    [GeneratedRegex(@"<!\[(?:if[^\]]*|endif)\]>", RegexOptions.IgnoreCase)]
    private static partial Regex RevealedConditional();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"</?[a-z]+:[a-z]+\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixedTag();

    [GeneratedRegex(@"<(style|script|head|title)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Invisible();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t]*\n[ \t\n]*")]
    private static partial Regex BreakRun();

    [GeneratedRegex(@"<br\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    // Mehrere Enden hintereinander (</li></ul>) sind ein Umbruch
    [GeneratedRegex(@"(?:</(?:p|div|li|tr|h[1-6]|table|ul|ol|blockquote|pre)\s*>\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"</t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex CellEnd();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    /// <summary>
    /// Das Stück HTML, das kopiert wurde, aus dem Inhalt von "HTML Format": ohne Kopfzeilen, ohne das Gerüst drumherum und
    /// ohne Office-Ballast. Leerraum über mehrere Zeilen wird zu einem Umbruch, damit es im Editor lesbar bleibt (in HTML
    /// zählt er ohnehin nur als Leerzeichen).
    /// </summary>
    public static string CleanFragment(string clipboardHtml)
    {
        var html = Fragment().Match(clipboardHtml) is { Success: true } fragment ? fragment.Groups[1].Value
            : Body().Match(clipboardHtml) is { Success: true } body ? body.Groups[1].Value
            : clipboardHtml;
        html = HiddenConditional().Replace(html, "");
        html = RevealedConditional().Replace(html, "");
        html = Comment().Replace(html, "");
        html = PrefixedTag().Replace(html, "");
        html = html.ReplaceLineEndings("\n");
        html = BreakRun().Replace(html, "\n");
        return html.Trim();
    }

    /// <summary>
    /// Wie der formatierte Text als reiner Text aussieht: für Programme, die nur Text nehmen, und fürs Tippen. Absätze,
    /// Zeilenumbrüche und Listen werden zu Zeilen, Tabellenzellen zu Tabs.
    /// </summary>
    public static string ToPlain(string html)
    {
        var text = Invisible().Replace(html, "");
        text = Comment().Replace(text, "");
        text = Whitespace().Replace(text, " ");
        text = LineBreak().Replace(text, "\n");
        text = BlockEnd().Replace(text, "\n");
        text = ListItem().Replace(text, "• ");
        text = CellEnd().Replace(text, "\t");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        var lines = text.Split('\n').Select(line => line.Trim(' ').TrimEnd('\t'));
        return string.Join("\n", lines).Trim('\n');
    }

    /// <summary>Text, der in HTML genauso dastehen soll: &amp; &lt; &gt; maskiert, Umbrüche als &lt;br&gt;.</summary>
    public static string Escape(string text)
    {
        var html = new StringBuilder(text.Length);
        foreach (var c in text.ReplaceLineEndings("\n"))
        {
            html.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\n' => "<br>",
                _ => c.ToString(),
            });
        }
        return html.ToString();
    }

    const string HeaderFormat = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
    const string Prefix = "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
    const string Suffix = "<!--EndFragment--></body></html>";

    /// <summary>
    /// Das Format "HTML Format" (CF_HTML) als UTF-8-Bytes: Kopfzeilen mit den Stellen von Dokument und Fragment, in Bytes
    /// gezählt, dann das Dokument. Word, Outlook, Teams und Browser lesen formatierten Text so.
    /// </summary>
    public static byte[] ClipboardFormat(string fragment)
    {
        var headerLength = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, HeaderFormat, 0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(Prefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(Suffix);
        var header = string.Format(CultureInfo.InvariantCulture, HeaderFormat, headerLength, endHtml, startFragment, endFragment);
        return Encoding.UTF8.GetBytes(header + Prefix + fragment + Suffix);
    }
}
