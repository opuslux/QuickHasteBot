using System.Net;

namespace QuickHasteBot.Core;

/// <summary>
/// Baut den Prompt, mit dem eine KI einen {{ … }}-Block für <see cref="SnippetScript"/> schreibt, und die URL dazu.
/// Die Regeln sind englisch: kürzer in der URL (keine kodierten Umlaute) und für die KI eindeutiger.
/// Google AI Mode sucht gern statt zu antworten: darum der Auftrag zuerst, keine Namen wie "Jint",
/// die eine Suche auslösen, und am Ende Beispiele für das erwartete Format.
/// </summary>
public static class ScriptPrompt
{
    /// <summary>
    /// Standard ist ChatGPT: gratis ohne Konto, schickt ?q= direkt ab und hält sich ans Format.
    /// Platzhalter {prompt} und {lang}, in settings.json als AiPromptUrl auch frei änderbar.
    /// </summary>
    public const string DefaultUrl = "https://chatgpt.com/?q={prompt}";

    /// <summary>Zur Auswahl in den Einstellungen und im Prompt-Fenster. Alle übernehmen den Prompt aus der Adresse.</summary>
    public static readonly IReadOnlyList<AiService> Services =
    [
        new("ChatGPT", DefaultUrl),
        new("Google AI Mode", "https://www.google.com/search?udm=50&hl={lang}&q={prompt}"),
        new("Perplexity", "https://www.perplexity.ai/search?q={prompt}"),
    ];

    /// <summary>Der Dienst zur Adresse, null bei einer selbst eingetragenen.</summary>
    public static AiService? ServiceFor(string? url) => Services.FirstOrDefault(s => s.Url == url);

    /// <summary>Platzhalter für die Eingabe im KI-Fenster, der einzige in einer Vorlage.</summary>
    public const string TaskPlaceholder = "{task}";

    /// <summary>
    /// Der eingebaute Prompt, auch Inhalt einer neu angelegten ai-prompt.txt. Kurz halten: Mit einem langen
    /// Codebeispiel antwortete Google AI Mode gar nicht mehr. Die Antwort kommt als Codeblock:
    /// Darin zeigen Chats den Code wörtlich und mit Kopieren-Knopf. Als Fliesstext würden Backticks zu Formatierung
    /// und [1] zu Quellenverweisen und gingen beim Kopieren verloren, die Regeln dazu sind die Absicherung.
    /// </summary>
    // Wie die Quelldatei ausgecheckt ist (LF oder CRLF), soll keine Rolle spielen
    public static readonly string DefaultTemplate = """
        Write JavaScript code for this task: {task}

        Answer with exactly one markdown code block and nothing else. The code inside starts with {{ and ends with }}.
        - Plain JavaScript only, no browser or Node APIs (fetch, window, document, require, setTimeout).
        - The value of the last expression is the result. Several statements: {{ (() => { ...; return value; })() }}
        - Never put two closing braces next to each other inside the code, "}}" ends it: write "} }".
        - No backticks or template literals, join strings with +. No numbers in square brackets like a[0], use a.at(0).
        - Global helpers, prefer them for dates and times (they use the Windows language):
        {helpers}

        Examples:
        Task "long date": {{ date('dddd, dd. MMMM yyyy') }}
        Task "date and time": {{ date('dd.MM.yyyy') + ' ' + time('HH:mm') }}
        """.ReplaceLineEndings("\n")
        .Replace("{helpers}", string.Join("\n", SnippetScript.Helpers.Where(h => InPrompt(h.Signature))
            .Select(h => $"  {h.Signature}: {h.Description}")));

    /// <summary>
    /// run() nicht: Die KI soll keine Programme starten. dblclick() und rclick() auch nicht, die braucht ein Block kaum,
    /// und die Adresse bliebe sonst nicht kurz genug für Google. ask() und selection auch nicht: Im KI-Fenster gibt es
    /// weder ein Fenster mit Fragen noch ein Ziel, der Block liesse sich dort nicht testen. msg() und confirm() aus
    /// demselben Grund nicht: Dort erscheint keine Meldung.
    /// </summary>
    internal static bool InPrompt(string signature) =>
        !new[] { "run(", "dblclick(", "rclick(", "ask(", "selection", "msg(", "confirm(" }.Any(name => signature.StartsWith(name, StringComparison.Ordinal));

    /// <param name="template">Inhalt von ai-prompt.txt, ohne Datei oder ohne {task} der eingebaute Prompt.</param>
    public static string Build(string task, string? template)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains(TaskPlaceholder))
            template = DefaultTemplate;
        return template.Replace(TaskPlaceholder, task.Trim()).Trim();
    }

    /// <summary>
    /// Macht aus einer KI-Antwort einen Block für einen Baustein. Wer den Codeblock markiert statt seinen
    /// Kopieren-Knopf zu nehmen, hat die ```-Zeilen oder die Sprachbeschriftung ("javascript") dabei, die fallen weg.
    /// Fehlt die Hülle {{ }}, weil sich die KI nicht ans Format hält, wird sie ergänzt.
    /// </summary>
    /// <param name="wrapped">true, wenn {{ }} ergänzt wurde.</param>
    public static string ToBlock(string answer, out bool wrapped)
    {
        var code = StripCodeFence(answer);
        wrapped = code.Length > 0 && !SnippetScript.HasBlocks(code);
        return wrapped ? "{{ " + code + " }}" : code;
    }

    static readonly string[] LanguageLabels = ["javascript", "js", "jsx", "typescript", "ts"];

    static string StripCodeFence(string text)
    {
        var lines = text.Trim().ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines.Count > 0 && (lines[0].TrimStart().StartsWith("```") || LanguageLabels.Contains(lines[0].Trim(), StringComparer.OrdinalIgnoreCase)))
            lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "```")
            lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    /// <summary>
    /// Setzt den Prompt kodiert für {prompt} und die Sprache für {lang} ein. Ohne {prompt} gilt <see cref="DefaultUrl"/>.
    /// Kodiert wie ein Formular: Leerzeichen als +, Klammern bleiben. Das spart viel Länge, Google kappt die Anfrage um 2048 Zeichen.
    /// </summary>
    public static string Url(string? template, string prompt, string language)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{prompt}"))
            template = DefaultUrl;
        return template
            .Replace("{lang}", WebUtility.UrlEncode(language))
            .Replace("{prompt}", WebUtility.UrlEncode(prompt));
    }
}

public sealed record AiService(string Name, string Url)
{
    public override string ToString() => Name;
}
