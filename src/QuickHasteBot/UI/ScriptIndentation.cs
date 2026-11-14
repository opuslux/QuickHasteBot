using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Indentation;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Einrücken beim Tippen im Editor: Enter übernimmt die Einrückung der Zeile davor, nach {if,…}, {elseif,…} und {else}
/// eine Ebene mehr. Wird {else}, {elseif,…} oder {/if} allein auf einer Zeile fertig getippt, rückt die Zeile auf ihre
/// Ebene zurück. Alles nur Layout (<see cref="ScriptIndent"/>).
/// </summary>
internal sealed class ScriptIndentation : IIndentationStrategy
{
    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (line.PreviousLine is not { } previous) return;
        var indentation = TextUtilities.GetWhitespaceAfter(document, previous.Offset);
        var indent = document.GetText(indentation);
        if (ScriptIndent.OpensBlock(document.GetText(previous)))
            indent += ScriptIndent.Unit;
        var current = TextUtilities.GetWhitespaceAfter(document, line.Offset);
        document.Replace(current.Offset, current.Length, indent, OffsetChangeMappingType.RemoveAndInsert);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
    }

    /// <summary>Nach einem getippten "}": {else}, {elseif,…} oder {/if} allein auf der Zeile auf seine Ebene setzen.</summary>
    public static void OnTextEntered(TextArea area, string text)
    {
        if (text != "}") return;
        var document = area.Document;
        var line = document.GetLineByOffset(area.Caret.Offset);
        if (!ScriptIndent.ClosesBlock(document.GetText(line))) return;
        foreach (var change in ScriptIndent.Changes(document.Text, line.Offset, line.Offset).Where(c => c.Start == line.Offset))
            document.Replace(change.Start, change.Length, change.Indent);
    }
}
