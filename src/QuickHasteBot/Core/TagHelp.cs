using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Was an einer Stelle im Bausteintext steht und welcher Hilfetext dazu passt: für den Tooltip im Editor.
/// </summary>
/// <param name="Start">Beginn des Tags bzw. Helfernamens im Text.</param>
/// <param name="Key">
/// Name des Texts mit der Kurzbeschreibung, wie im Menü "Tag einfügen" (z.B. "TagNextField"); der Hilfetext heisst
/// gleich mit "Help" dahinter.
/// </param>
/// <param name="KeyName">Bei <see cref="TagHelp.AnyKey"/>: die Taste, wie sie im Tag steht, z.B. "ctrl+s".</param>
/// <param name="Repeated">Eine Taste mit Anzahl, z.B. {down,3}: Dazu passt auch die Hilfe zur Wiederholung.</param>
public sealed record HelpTopic(int Start, int Length, string Key, string? KeyName = null, bool Repeated = false);

public static class TagHelp
{
    /// <summary>Eine Taste ohne eigenen Text, z.B. {pgup} oder {ctrl+s}: der Hilfetext nimmt den Namen als {0}.</summary>
    public const string AnyKey = "TagKey";

    /// <summary>Die Hilfe zur Wiederholung, für <see cref="HelpTopic.Repeated"/>.</summary>
    public const string Repeat = "TagRepeat";

    // Tasten mit eigenem Text, wie im Menü. Gleichbedeutende Namen ({bs}, {backspace}) zeigen denselben.
    static readonly Dictionary<string, string> KeyTopics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tab"] = "TagNextField", ["shift+tab"] = "TagPreviousField", ["enter"] = "TagSubmit", ["space"] = "TagCheckbox",
        ["esc"] = "TagCancel", ["escape"] = "TagCancel", ["up"] = "TagUp", ["down"] = "TagDown", ["left"] = "TagLeft",
        ["right"] = "TagRight", ["home"] = "TagHome", ["end"] = "TagEnd", ["bs"] = "TagBackspace", ["backspace"] = "TagBackspace",
        ["del"] = "TagDelete", ["delete"] = "TagDelete", ["ctrl+a"] = "TagSelectAll", ["alt+down"] = "TagOpenDropdown",
        ["ctrl+enter"] = "TagSendChat", ["f5"] = "TagReload", ["numplus"] = "TagNumPlus", ["numminus"] = "TagNumMinus",
        ["nummul"] = "TagNumMul", ["numstar"] = "TagNumMul", ["ctrl+plus"] = "TagZoomIn", ["prtsc"] = "TagPrintScreen",
        ["printscreen"] = "TagPrintScreen", ["playpause"] = "TagPlayPause", ["browserback"] = "TagBrowserBack",
    };

    // Die Helfer in {{ … }}; focus, click, set, dblclick, rclick, window und wait erklären sich wie ihr Tag
    static readonly Dictionary<string, string> HelperTopics = new()
    {
        ["date"] = "TagDate", ["time"] = "TagTime", ["isoweek"] = "TagWeek", ["clipboard"] = "TagClipboard", ["selection"] = "TagSelection", ["ask"] = "TagAsk", ["user"] = "TagUser",
        ["guid"] = "TagGuid", ["key"] = "TagKeyHelper", ["wait"] = "TagWait", ["focus"] = "TagFocus", ["click"] = "TagClick",
        ["set"] = "TagSet", ["dblclick"] = "TagDoubleClick", ["rclick"] = "TagRightClick", ["window"] = "TagWindow",
        ["run"] = "TagRunOptions",
    };

    /// <summary>Alle Texte, auf die <see cref="At"/> zeigen kann, für den Test, dass es sie gibt.</summary>
    public static IEnumerable<string> AllKeys =>
        KeyTopics.Values.Concat(HelperTopics.Values)
            .Concat(["TagComment", "TagWait", "TagDelay", "TagClear", "TagGuardOff", "TagGuardOn", "TagFocus", "TagClick", "TagSet",
                "TagDoubleClick", "TagRightClick", "TagWindow", "TagStart", "TagStartUrl", "TagLetterKey", "TagCursor", "TagHtml", "TagInclude",
                AnyKey, Repeat])
            .Distinct();

    /// <summary>
    /// Das Tag, der Kommentar oder der Skript-Helfer an <paramref name="offset"/>, null wenn dort Text steht. Auch ein
    /// fehlerhaftes Tag wie {tab,x} hat seine Hilfe. Folgt denselben Regeln wie die Farben (<see cref="SnippetSyntax"/>).
    /// </summary>
    public static HelpTopic? At(string text, int offset, bool keyTags = true, bool scripts = true)
    {
        foreach (Match match in KeyTags.Candidate().Matches(text))
        {
            if (match.Index > offset) break;
            if (offset >= match.Index + match.Length) continue;

            if (match.Groups["note"].Success)
                return keyTags ? new(match.Index, match.Length, "TagComment") : null;
            if (!match.Groups[1].Success)
                return scripts ? Helper(text, match.Index, match.Length, offset) : null;
            return keyTags && Tag(match.Groups[1].Value) is { } topic ? topic with { Start = match.Index, Length = match.Length } : null;
        }
        return null;
    }

    static HelpTopic? Tag(string content)
    {
        if (KeyTags.IsHtmlEnd(content)) return new(0, 0, "TagHtml");
        var name = KeyTags.NamePart().Match(content);
        if (!name.Success || !KeyTags.IsTagName(name.Groups[1].Value)) return null; // {name}, JSON: Text

        var spec = name.Groups[1].Value.ToLowerInvariant();
        var args = KeyTags.SplitArgs(content[name.Length..]) ?? [];
        var first = args.Count > 0 ? args[0].Trim() : "";
        var key = spec switch
        {
            "wait" => "TagWait",
            "delay" => "TagDelay",
            "clear" => "TagClear",
            "guard" => first.Equals("on", StringComparison.OrdinalIgnoreCase) ? "TagGuardOn" : "TagGuardOff",
            "focus" => "TagFocus",
            "click" => "TagClick",
            "set" => "TagSet",
            "dblclick" => "TagDoubleClick",
            "rclick" => "TagRightClick",
            "window" => "TagWindow",
            "start" => KeyTags.IsWebAddress(first) ? "TagStartUrl" : "TagStart",
            "key" => "TagLetterKey",
            "cursor" => "TagCursor",
            "html" => "TagHtml",
            "include" => "TagInclude",
            _ => null,
        };
        if (key is not null) return new(0, 0, key);

        var repeated = args.Count == 1 && int.TryParse(first, out var n) && n > 1;
        return KeyTopics.TryGetValue(spec, out var topic)
            ? new(0, 0, topic, Repeated: repeated)
            : new(0, 0, AnyKey, spec, repeated);
    }

    // Ein Helfername im Block, nicht als Eigenschaft (x.date) und nicht in Zeichenketten oder Kommentaren
    static HelpTopic? Helper(string text, int start, int length, int offset)
    {
        var codeStart = start + 2;
        var code = text.Substring(codeStart, length - 4);
        foreach (Match token in SnippetSyntax.ScriptToken().Matches(code))
        {
            if (codeStart + token.Index > offset) break;
            if (offset >= codeStart + token.Index + token.Length) continue;
            return token.Groups["i"].Success && HelperTopics.TryGetValue(token.Value, out var key) && !SnippetSyntax.PrecededByDot(code, token.Index)
                ? new(codeStart + token.Index, token.Length, key)
                : null;
        }
        return null;
    }
}
