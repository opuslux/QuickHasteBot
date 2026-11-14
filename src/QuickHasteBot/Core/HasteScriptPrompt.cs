using System.Text;
using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Was "HasteScript AI" über das Skript im Editor weiss: Aufgabe, Text, Markierung und was der Editor daran bemängelt.
/// </summary>
/// <param name="Task">Was die KI tun soll, leer: Fehler suchen und verbessern.</param>
/// <param name="Problems">Rote Tags und Skriptfehler, schon als Text, z.B. "Line 3: {tab,x} – …".</param>
/// <param name="Culture">Die Windows-Sprache, nach ihr richten sich date() und time(), z.B. "de-CH".</param>
/// <param name="Buttons">
/// Die Knöpfe, auf die die KI verweist: englisch wie im README → so, wie die Oberfläche sie zeigt. Auf Deutsch heisst
/// "Pick element" "Element erfassen", und die KI soll den Namen nennen, den man sieht.
/// </param>
public sealed record HasteScriptRequest(
    string Task,
    string Script,
    int SelectionStart,
    int SelectionLength,
    string Name,
    string Extension,
    string Version,
    string Culture,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, string>? Buttons = null);

/// <summary>
/// Baut den grossen Prompt für "HasteScript AI": Anweisungen, die Referenz aus dem README, das Skript aus dem Editor
/// und die Aufgabe. Er geht über die Zwischenablage in eine beliebige KI, für eine Adresse (?q=) ist er viel zu lang.
/// Englisch wie <see cref="ScriptPrompt"/>: Das README ist englisch, und die KI antwortet trotzdem in der Sprache der Aufgabe.
/// </summary>
public static partial class HasteScriptPrompt
{
    public const string TaskPlaceholder = "{task}";
    public const string DocsPlaceholder = "{docs}";
    public const string SnippetPlaceholder = "{snippet}";

    /// <summary>Für eine leere Aufgabe.</summary>
    public const string DefaultTask = "Check the script for errors and improve it.";

    /// <summary>
    /// Der eingebaute Prompt, auch Inhalt einer neu angelegten hastescript-prompt.txt. Die Anweisungen zuerst, die
    /// lange Referenz in der Mitte, Skript und Aufgabe am Ende: Was zuletzt steht, gewichten KIs am stärksten.
    /// Die KI sieht weder den Bildschirm noch das Zielprogramm: Selektoren soll sie nicht raten, sondern sagen, wo
    /// man sie mit dem Picker einfügt.
    /// </summary>
    public static readonly string DefaultTemplate = """
        You are an expert in HasteScript, the snippet language of QuickHasteBot, a Windows tool that pastes text snippets and fills in forms in other programs. A snippet is plain text with key tags like {tab}, element tags like {click,…}, conditions, comments and {{ … }} JavaScript blocks. The official reference is below. Help me with my script: fix errors, improve it or add what I ask for.

        How to answer, in the language of my request:
        1. In a few short sentences: what was wrong or what you changed, and why.
        2. The complete new script in exactly one markdown code block, ready to replace the old one. Never shorten it with "..." or "rest unchanged".
        3. Only if needed, numbered steps I have to do myself. You cannot see my screen or the program the snippet runs in, so never guess a selector for a button, field, menu or window you don't know. Leave that spot out of the script and tell me instead: which element it is, where it belongs (quote the line), and to put the cursor there in the editor, click "Pick element", drag the crosshair onto the element, choose the action if needed and click "Insert into editor".
        If my request is unclear, first ask me up to three short questions instead of writing the script.

        Rules:
        - Use only tags, helpers and options from the reference. Never invent any. If something is not possible, say so and suggest the closest way that works.
        - Plain text is typed exactly as written, including spaces and line breaks. Keep my wording and layout unless I ask to change it.
        - In snippets with tags, indentation at the start of a line is only layout: indent the content of {if} blocks by 4 spaces per level. For spaces that must really be typed at the start of a line, use {space,n}.
        - Keep existing selectors exactly as they are, they were picked from the real program.
        - Inside {{ … }} the first "}}" ends the block. Only where your JavaScript needs two closing curly braces directly in a row, put a space between them: "} }". Anything in between already separates them, so "{ icon: 'info' }) }}" stays as it is.
        - Change only what my request needs and copy every other character of the script exactly, including parentheses, quotes and commas. Before answering, check that each {{ … }} block is valid JavaScript with all ( [ { closed and all strings ended.
        - To check the result, point me to "Test" and "Step by step" in the editor.

        # HasteScript reference

        {docs}

        # My snippet

        {snippet}

        # My request

        {task}
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// Die Abschnitte des README, die das Skript selbst betreffen, jeweils mit ihren Unterabschnitten. Der Rest
    /// (Popup, Einstellungen, Installation) ist Bedienung und würde den Prompt nur verlängern.
    /// </summary>
    internal static readonly string[] DocSections =
    [
        "### Snippets in snippets",
        "## Formatted text",
        "## Editor",
        "## Scripts",
        "## Forms: key tags",
    ];

    /// <summary>Die KI-Fenster selbst: Für die KI nutzlos, und sie soll keinen Prompt im Prompt schreiben.</summary>
    internal static readonly string[] SkippedSections =
    [
        "### Writing a block with AI",
        "### Improving a script with HasteScript AI",
    ];

    /// <param name="template">Inhalt von hastescript-prompt.txt, ohne Datei der eingebaute Prompt.</param>
    /// <param name="readme">Das README, aus dem die Referenz geschnitten wird.</param>
    public static string Build(HasteScriptRequest request, string? template, string readme)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = DefaultTemplate;
        var task = request.Task.Trim().Length > 0 ? request.Task.Trim() : DefaultTask;
        var parts = new (string Placeholder, string Heading, string Text)[]
        {
            (DocsPlaceholder, "# HasteScript reference", Docs(readme)),
            (SnippetPlaceholder, "# My snippet", Snippet(request)),
            (TaskPlaceholder, "# My request", task),
        };

        // In einem Durchgang: Ein "{task}" im Skript oder in der Referenz bleibt so stehen
        template = template.ReplaceLineEndings("\n").Trim();
        var prompt = new StringBuilder(Placeholders().Replace(template,
            m => parts.First(p => p.Placeholder == m.Value).Text));
        // Eine eigene Vorlage ohne Platzhalter verliert nichts: Was fehlt, kommt hinten dran
        foreach (var (placeholder, heading, text) in parts.Where(p => !template.Contains(p.Placeholder)))
            prompt.Append("\n\n").Append(heading).Append("\n\n").Append(text);
        return prompt.ToString().Trim() + "\n";
    }

    [GeneratedRegex(@"\{(?:task|docs|snippet)\}")]
    private static partial Regex Placeholders();

    /// <summary>
    /// Schneidet <see cref="DocSections"/> aus dem README, ohne <see cref="SkippedSections"/>. Überschriften in
    /// Codeblöcken zählen nicht.
    /// </summary>
    public static string Docs(string readme)
    {
        var result = new StringBuilder();
        var keepLevel = 0;  // Ebene der Überschrift, deren Abschnitt gerade übernommen wird, 0: keiner
        var skipLevel = 0;
        var fence = false;
        foreach (var line in readme.ReplaceLineEndings("\n").Split('\n'))
        {
            // Auch eingerückt in Listen
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                fence = !fence;
            else if (!fence && HeadingLevel(line) is var level and > 0)
            {
                if (keepLevel > 0 && level <= keepLevel) keepLevel = 0;
                if (skipLevel > 0 && level <= skipLevel) skipLevel = 0;
                var heading = line.TrimEnd();
                if (keepLevel == 0 && DocSections.Contains(heading)) keepLevel = level;
                if (keepLevel > 0 && SkippedSections.Contains(heading)) skipLevel = level;
            }
            if (keepLevel > 0 && skipLevel == 0)
                result.Append(line).Append('\n');
        }
        return result.ToString().Trim();
    }

    static int HeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;
        return level is > 0 and <= 6 && level < line.Length && line[level] == ' ' ? level : 0;
    }

    /// <summary>Angaben zum Baustein, was der Editor bemängelt, die Markierung und das Skript selbst.</summary>
    internal static string Snippet(HasteScriptRequest request)
    {
        var text = new StringBuilder();
        var name = request.Name.Trim().Length > 0 ? request.Name.Trim() : "(new snippet)";
        text.Append($"Name: {name}{request.Extension}\n");
        text.Append($"QuickHasteBot {request.Version}, Windows language {request.Culture}\n");
        var renamed = (request.Buttons ?? new Dictionary<string, string>()).Where(b => b.Key != b.Value).ToList();
        if (renamed.Count > 0)
            text.Append("My editor shows the buttons in my language, use these names: ")
                .Append(string.Join(", ", renamed.Select(b => $"\"{b.Value}\" ({b.Key})")))
                .Append('\n');

        if (request.Problems.Count > 0)
        {
            text.Append("\nProblems found by the editor:\n");
            foreach (var problem in request.Problems)
                text.Append("- ").Append(problem).Append('\n');
        }

        var script = request.Script.ReplaceLineEndings("\n");
        if (script.Trim().Length == 0)
        {
            text.Append("\nThe script is empty: write a new one.\n");
            return text.ToString().Trim();
        }

        var (start, length) = (Math.Clamp(request.SelectionStart, 0, request.Script.Length), request.SelectionLength);
        length = Math.Clamp(length, 0, request.Script.Length - start);
        if (length > 0 && request.Script.Substring(start, length).Trim().Length > 0)
        {
            var first = LineAt(request.Script, start);
            var last = LineAt(request.Script, start + length - 1);
            var lines = first == last ? $"line {first}" : $"lines {first}-{last}";
            var selected = request.Script.Substring(start, length).ReplaceLineEndings("\n");
            text.Append($"\nI selected this part ({lines}). Change only this part unless my request needs more, but still answer with the complete script:\n");
            text.Append(Fenced(selected));
        }

        text.Append("\nThe script:\n");
        text.Append(Fenced(script));
        return text.ToString().Trim();
    }

    /// <summary>Zeilennummer ab 1, wie im Editor.</summary>
    internal static int LineAt(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    /// <summary>In einen Codeblock, dessen Zaun länger ist als jede Backtick-Folge im Text: so endet er nicht zu früh.</summary>
    internal static string Fenced(string text)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        return fence + "\n" + text.TrimEnd('\n') + "\n" + fence + "\n";
    }
}
