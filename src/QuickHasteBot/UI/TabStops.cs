using System.Text;

namespace QuickHasteBot.UI;

/// <summary>
/// Tabs für die Textvorschauen (Popup, Editor). Ein TextBlock setzt Tabstopps in Pixeln (4 × Schriftgrösse),
/// mit Tabs ausgerichtete Spalten stünden dort anders als im Editor (AvalonEdit: alle 4 Zeichen).
/// </summary>
internal static class TabStops
{
    /// <summary>Tabs als Leerzeichen bis zum nächsten Tabstopp alle <paramref name="size"/> Zeichen, pro Zeile gezählt.</summary>
    public static string Expand(string text, int size = 4)
    {
        if (!text.Contains('\t')) return text;
        var result = new StringBuilder(text.Length + 16);
        var column = 0;
        foreach (var c in text)
        {
            switch (c)
            {
                case '\t':
                    var spaces = size - column % size;
                    result.Append(' ', spaces);
                    column += spaces;
                    break;
                case '\r' or '\n':
                    result.Append(c);
                    column = 0;
                    break;
                default:
                    result.Append(c);
                    column++;
                    break;
            }
        }
        return result.ToString();
    }
}
