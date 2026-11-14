using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Ein Eintrag im Tag-Menü. <see cref="Select"/>: was nach dem Einfügen markiert wird, zum Überschreiben.
/// <see cref="OwnLine"/>: auf eine eigene Zeile wie die Tags aus dem Picker (Kommentare).
/// </summary>
/// <param name="Description">
/// Name des Texts in <see cref="Strings"/> mit der Kurzbeschreibung rechts im Menü, z.B. nameof(Strings.TagNextField):
/// angezeigt in der Sprache der Oberfläche, der Filter findet ihn auch auf Englisch. Der Hilfetext im Tooltip heisst
/// gleich mit "Help" dahinter (<see cref="Help"/>).
/// </param>
/// <param name="FromClipboard">Statt <see cref="Tag"/> den formatierten Text aus der Zwischenablage als {html}-Block.</param>
/// <param name="Text">Was eingefügt wird, wenn es nicht <see cref="Tag"/> ist, z.B. ein ganzer {if}-Block über mehrere Zeilen.</param>
internal sealed record TagEntry(string Tag, string Description, string? Select = null, bool OwnLine = false, bool FromClipboard = false,
    string? Text = null)
{
    public string Help => Description + "Help";
}

/// <summary>Was das Menü „Tag einfügen ▾“ im Editor anbietet, ohne die Oberfläche (<see cref="TagMenuBuilder"/>).</summary>
internal static class TagCatalog
{
    /// <summary>
    /// Die Einträge von "Tag einfügen ▾", nach Gruppen (Namen der Texte), null ist ein Trenner. Ein Test prüft, dass
    /// jeder Eintrag Kurzbeschreibung und Hilfetext in allen Sprachen hat.
    /// </summary>
    internal static (string Header, TagEntry?[] Entries)[] Groups() =>
    [
        (nameof(Strings.EditorTagsKeys),
        [
            new("{tab}", nameof(Strings.TagNextField)), new("{shift+tab}", nameof(Strings.TagPreviousField)),
            new("{enter}", nameof(Strings.TagSubmit)), new("{space}", nameof(Strings.TagCheckbox)), new("{esc}", nameof(Strings.TagCancel)), null,
            new("{up}", nameof(Strings.TagUp)), new("{down}", nameof(Strings.TagDown)), new("{down,3}", nameof(Strings.TagRepeat)),
            new("{left}", nameof(Strings.TagLeft)), new("{right}", nameof(Strings.TagRight)), new("{home}", nameof(Strings.TagHome)),
            new("{end}", nameof(Strings.TagEnd)), null,
            new("{bs}", nameof(Strings.TagBackspace)), new("{del}", nameof(Strings.TagDelete)), new("{ctrl+a}", nameof(Strings.TagSelectAll)),
            new("{alt+down}", nameof(Strings.TagOpenDropdown)), new("{ctrl+enter}", nameof(Strings.TagSendChat)), new("{f5}", nameof(Strings.TagReload)), null,
            new("{numplus}", nameof(Strings.TagNumPlus)), new("{numminus}", nameof(Strings.TagNumMinus)), new("{nummul}", nameof(Strings.TagNumMul)),
            new("{ctrl+plus}", nameof(Strings.TagZoomIn)), new("{key,a}", nameof(Strings.TagLetterKey), "a"), null,
            new("{prtsc}", nameof(Strings.TagPrintScreen)), new("{playpause}", nameof(Strings.TagPlayPause)), new("{browserback}", nameof(Strings.TagBrowserBack)),
        ]),
        (nameof(Strings.EditorTagsFlow),
        [
            new("{# " + Strings.TagCommentText + "}", nameof(Strings.TagComment), Strings.TagCommentText, OwnLine: true), null,
            new("{wait,500}", nameof(Strings.TagWait), "500"), new("{delay,50}", nameof(Strings.TagDelay), "50"),
            new("{clear}", nameof(Strings.TagClear)), new("{cursor}", nameof(Strings.TagCursor)), null,
            new("{include,Folder/Name}", nameof(Strings.TagInclude), "Folder/Name"),
            new("{html}…{/html}", nameof(Strings.TagHtmlPaste), FromClipboard: true),
            new("{html}<b>" + Strings.TagHtmlText + "</b>{/html}", nameof(Strings.TagHtml), Strings.TagHtmlText), null,
            new("{if,name=Label}…{else}…{/if}", nameof(Strings.TagIf), "Label", OwnLine: true, Text: IfBlock("name=Label")),
            new("{if,process=program}…{/if}", nameof(Strings.TagIfWindow), "program", OwnLine: true, Text: IfBlock("process=program")),
            new("{if,js=\"x == 1\"}…{/if}", nameof(Strings.TagIfScript), "x == 1", OwnLine: true, Text: IfBlock("js=\"x == 1\"")), null,
            new("{guard,off}", nameof(Strings.TagGuardOff)), new("{guard,on}", nameof(Strings.TagGuardOn)),
        ]),
        (nameof(Strings.EditorTagsElements),
        [
            new("{focus,#id}", nameof(Strings.TagFocus), "id"), new("{click,name=Label}", nameof(Strings.TagClick), "Label"),
            new("{set,#id,\"\"}", nameof(Strings.TagSet), "id"),
            new("{dblclick,name=Label}", nameof(Strings.TagDoubleClick), "Label"), new("{rclick,name=Label}", nameof(Strings.TagRightClick), "Label"), null,
            new("{window,program}", nameof(Strings.TagWindow), "program"), new("{start,program}", nameof(Strings.TagStart), "program"),
            new("{start,https://example.com}", nameof(Strings.TagStartUrl), "https://example.com"),
        ]),
        (nameof(Strings.EditorTagsScripts),
        [
            new("{{ date() }}", nameof(Strings.TagDate)), new("{{ date('dd.MM.yyyy', 14) }}", nameof(Strings.TagDateIn), "14"),
            new("{{ time() }}", nameof(Strings.TagTime)), new("{{ isoweek() }}", nameof(Strings.TagWeek)),
            new("{{ clipboard }}", nameof(Strings.TagClipboard)), new("{{ selection }}", nameof(Strings.TagSelection)),
            new("{{ ask('" + Strings.TagAskLabel + "') }}", nameof(Strings.TagAsk), Strings.TagAskLabel), new("{{ user }}", nameof(Strings.TagUser)),
            new("{{ guid() }}", nameof(Strings.TagGuid)), null,
            new("{{ const r = run('d:/scripts/file.bat') }}", nameof(Strings.TagRun), "d:/scripts/file.bat", OwnLine: true),
            new("{{ const r = run('d:/scripts/file.bat', { args: '', dir: 'd:/data' }) }}", nameof(Strings.TagRunOptions), "d:/scripts/file.bat", OwnLine: true),
            new("{{ run('powershell', '-NoProfile -Command \"$env:COMPUTERNAME\"', { hidden: true }).out.trim() }}", nameof(Strings.TagPowerShell), "$env:COMPUTERNAME"),
            new("{{ msg('" + Strings.TagMessageText + "') }}", nameof(Strings.TagMessage), Strings.TagMessageText, OwnLine: true),
            new("{{ msg('" + Strings.TagMessageText + "', { title: 'QuickHasteBot', icon: 'warning', close: 5 }) }}", nameof(Strings.TagMessageOptions),
                Strings.TagMessageText, OwnLine: true),
            new("{if,js=\"confirm('" + Strings.TagConfirmText + "')\"}…{/if}", nameof(Strings.TagConfirm), Strings.TagConfirmText, OwnLine: true,
                Text: IfBlock("js=\"confirm('" + Strings.TagConfirmText + "')\"")),
        ]),
    ];

    /// <summary>Ein leerer {if}-Block mit {else}, je eine eingerückte Leerzeile für die beiden Zweige.</summary>
    static string IfBlock(string condition) => "{if," + condition + "}\n" + ScriptIndent.Unit + "\n{else}\n" + ScriptIndent.Unit + "\n{/if}";
}
