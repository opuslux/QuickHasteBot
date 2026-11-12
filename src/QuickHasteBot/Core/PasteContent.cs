using System.Globalization;
using System.Text;

namespace QuickHasteBot.Core;

/// <summary>
/// Ein Baustein, der ohne Tippen von Tasten auskommt, auch mit {cursor} und {html}…{/html}: Er wird wie Text eingefügt,
/// mit der eingestellten Methode, statt Zeichen für Zeichen getippt.
/// </summary>
/// <param name="Text">Der reine Text, {html}-Blöcke als Klartext. Das wird getippt bzw. ist der Text in der Zwischenablage.</param>
/// <param name="Html">Alles als HTML, wenn ein {html}-Block darin ist (Text drumherum maskiert), sonst null.</param>
/// <param name="CaretBack">So oft nach dem Einfügen Pfeil links, damit die Schreibmarke bei {cursor} steht.</param>
public sealed record PasteContent(string Text, string? Html, int CaretBack)
{
    /// <summary>Ob sich diese Schritte so einfügen lassen: nur Text, {cursor} und {html}-Blöcke.</summary>
    public static bool Fits(IEnumerable<FormStep> steps) => steps.All(s => s is TextStep or CursorStep or HtmlStep);

    /// <summary>Setzt die Schritte zusammen. Nur das erste {cursor} zählt.</summary>
    public static PasteContent From(IEnumerable<FormStep> steps)
    {
        var text = new StringBuilder();
        StringBuilder? html = null;
        int? cursor = null;
        foreach (var step in steps)
        {
            switch (step)
            {
                case TextStep t:
                    text.Append(t.Text);
                    html?.Append(HtmlText.Escape(t.Text));
                    break;
                case HtmlStep h:
                    html ??= new StringBuilder(HtmlText.Escape(text.ToString()));
                    text.Append(HtmlText.ToPlain(h.Html));
                    html.Append(h.Html);
                    break;
                case CursorStep:
                    cursor ??= text.Length;
                    break;
            }
        }
        var all = text.ToString();
        return new(all, html?.ToString(), cursor is { } at ? Positions(all[at..]) : 0);
    }

    /// <summary>
    /// Wie oft Pfeil links über diesen Text geht: ein Zeilenumbruch (auch \r\n) und ein Emoji oder Umlaut mit Akzent sind
    /// je eine Stelle.
    /// </summary>
    public static int Positions(string text) =>
        text.Length == 0 ? 0 : new StringInfo(text.ReplaceLineEndings("\n")).LengthInTextElements;
}
