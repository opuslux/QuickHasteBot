using Jint;

namespace QuickHasteBot.Core;

/// <summary>
/// Die Skripte eines Bausteins in einer Engine, auch über mehrere Durchgänge: Variablen aus einem Block gelten in allen
/// folgenden, Fehlermeldungen zählen die Blöcke durchgehend.
/// Normalerweise werden alle {{ … }} ausgewertet, bevor die erste Taste gedrückt wird (<see cref="Render"/>). Damit
/// run() an seiner Stelle läuft, wertet <see cref="RenderStaged"/> ab dem ersten Block mit run() nichts mehr aus, sondern
/// setzt <see cref="DeferredStep"/>s ein. Die wertet der Lauf dann mit <see cref="Evaluate"/> aus, wenn er dort ankommt.
/// </summary>
public sealed class ScriptSession : IDisposable
{
    readonly Engine _engine;
    readonly ScriptContext _context;
    int _block;

    public ScriptSession(ScriptContext context)
    {
        _context = context;
        _engine = SnippetScript.CreateEngine(context);
    }

    public void Dispose() => _engine.Dispose();

    /// <exception cref="SnippetScriptException">Syntax- oder Laufzeitfehler, Zeit- oder Speicherlimit in einem Block.</exception>
    public string Render(string text) => Render(text, staged: false);

    /// <summary>
    /// Wie <see cref="Render"/>, aber der erste Block mit run() und alle danach werden zu <see cref="DeferredStep"/>s.
    /// Braucht <see cref="ScriptContext.Form"/>.
    /// </summary>
    public string RenderStaged(string text) => Render(text, staged: true);

    /// <summary>
    /// Ob <see cref="RenderStaged"/> nötig ist: Vor dem ersten Block mit run() steht schon ein Tag (in
    /// <paramref name="marked"/> ein Marker), es passiert also etwas, bevor das Skript laufen soll. Sonst kann alles
    /// vorher ausgewertet werden, auch run().
    /// </summary>
    public static bool NeedsStaging(string marked)
    {
        foreach (System.Text.RegularExpressions.Match match in SnippetScript.Block().Matches(marked))
        {
            if (SnippetScript.CallsRun(match.Groups[1].Value))
                return KeyTags.HasMarkers(marked[..match.Index]);
        }
        return false;
    }

    /// <summary>Ob der Text irgendwo run() aufruft.</summary>
    public static bool UsesRun(string text) =>
        SnippetScript.Block().Matches(text).Any(m => SnippetScript.CallsRun(m.Groups[1].Value));

    string Render(string text, bool staged)
    {
        if (!SnippetScript.HasBlocks(text)) return text;
        var form = _context.Form;
        if (staged && form is null)
            throw new InvalidOperationException("staged scripts need key tags");

        var deferring = false;
        return SnippetScript.Block().Replace(text, match =>
        {
            var block = ++_block;
            var code = match.Groups[1].Value;
            form?.BeginScript(match.Index, match.Length);
            string output;
            if (staged && (deferring |= SnippetScript.CallsRun(code)))
                output = form!.Marker(new DeferredStep(block, code)); // Herkunft: der ganze Block
            else
                output = SnippetScript.Evaluate(_engine, code, block);
            form?.EndScript(match.Index, match.Length, output.Length);
            return output;
        });
    }

    /// <summary>
    /// Wertet einen zurückgestellten Block jetzt aus (run() läuft dabei) und gibt seine Ausgabe als Schritte zurück, alle
    /// mit <paramref name="source"/> als Herkunft. Blockiert, solange run() wartet: nicht auf dem UI-Thread aufrufen.
    /// </summary>
    /// <exception cref="SnippetScriptException">Fehler im Block, auch ein throw oder ein run(), das nicht fertig wurde.</exception>
    /// <exception cref="OperationCanceledException">Esc beim Warten auf run().</exception>
    public IReadOnlyList<SourcedStep> Evaluate(DeferredStep step, SourceSpan source)
    {
        var form = _context.Form ?? throw new InvalidOperationException("staged scripts need key tags");
        form.BeginDeferred(source);
        try
        {
            return KeyTags.SplitWithSources(SnippetScript.Evaluate(_engine, step.Code, step.Block), form, source);
        }
        finally
        {
            form.EndDeferred();
        }
    }
}
