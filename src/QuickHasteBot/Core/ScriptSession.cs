using System.Text;
using System.Text.RegularExpressions;
using Jint;

namespace QuickHasteBot.Core;

/// <summary>
/// Die Skripte eines Bausteins in einer Engine, auch über mehrere Durchgänge: Variablen aus einem Block gelten in allen
/// folgenden, Fehlermeldungen zählen die Blöcke durchgehend.
/// Normalerweise werden alle {{ … }} ausgewertet, bevor die erste Taste gedrückt wird (<see cref="Render"/>). Damit
/// run() an seiner Stelle läuft, wertet <see cref="RenderStaged"/> ab dem ersten Block mit run() nichts mehr aus, sondern
/// setzt <see cref="DeferredStep"/>s ein. Die wertet der Lauf dann mit <see cref="Evaluate"/> aus, wenn er dort ankommt.
/// Blöcke mit msg() oder confirm() gelten wie run(). Ebenso ab dem ersten {if,…}: Ein Block in einem Zweig läuft nur, wenn
/// der Zweig an der Reihe ist.
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
    /// <paramref name="marked"/> ein Marker), es passiert also etwas, bevor das Skript laufen soll. Oder vor einem Block
    /// steht ein {if,…} (aus <paramref name="form"/>): Ob er läuft, entscheidet sich erst unterwegs. Sonst kann alles
    /// vorher ausgewertet werden, auch run().
    /// </summary>
    public static bool NeedsStaging(string marked, FormBuilder? form = null)
    {
        var runSeen = false;
        foreach (Match match in SnippetScript.Block().Matches(marked))
        {
            if (form is not null && KeyTags.HasIfMarker(marked, match.Index, form))
                return true;
            if (!runSeen && SnippetScript.Waits(match.Groups[1].Value))
            {
                if (KeyTags.HasMarkers(marked[..match.Index]))
                    return true;
                runSeen = true; // Das erste run() darf vorher laufen, ein {if} danach entscheidet trotzdem
            }
        }
        return false;
    }

    /// <summary>
    /// Wertet die Bedingung aus {if,js="…"} jetzt aus, in derselben Engine wie die Blöcke, sieht also ihre Variablen.
    /// Blockiert, falls der Ausdruck run() aufruft: nicht auf dem UI-Thread aufrufen.
    /// </summary>
    /// <exception cref="SnippetScriptException">Fehler im Ausdruck (Block 0).</exception>
    /// <exception cref="OperationCanceledException">Esc beim Warten auf run().</exception>
    public bool Test(string expression) =>
        SnippetScript.Evaluate(_engine, "!!(" + expression + "\n)", 0) == "true";

    /// <summary>
    /// Führt die Blöcke in <paramref name="text"/> nur wegen ihrer Variablen aus, die Ausgabe fällt weg und Fehler werden
    /// übergangen. Für die Vorschau eines einzelnen Blocks: so kennt er, was die Blöcke davor festlegen. Die Nummern
    /// zählen weiter, ein Fehler im Block danach nennt also seine Nummer im ganzen Text.
    /// </summary>
    public void Prepare(string text)
    {
        foreach (Match match in SnippetScript.Block().Matches(text))
        {
            try
            {
                SnippetScript.Evaluate(_engine, match.Groups[1].Value, ++_block);
            }
            catch (SnippetScriptException)
            {
            }
        }
    }

    /// <summary>Ob der Text irgendwo run(), msg() oder confirm() aufruft, also auf etwas ausserhalb wartet.</summary>
    public static bool Waits(string text) =>
        SnippetScript.Block().Matches(text).Any(m => SnippetScript.Waits(m.Groups[1].Value));

    string Render(string text, bool staged)
    {
        if (!SnippetScript.HasBlocks(text)) return text;
        if (staged && _context.Form is null)
            throw new InvalidOperationException("staged scripts need key tags");
        return Expand(text, ref _block, staged, _context.Form);
    }

    /// <summary>
    /// Ersetzt die Blöcke durch ihre Ausgabe. Eine Zeile, die nur aus Blöcken besteht und nichts ausgibt, fällt samt
    /// Umbruch weg (wie ein Kommentar), so hinterlassen Zuweisungen und Prüfungen keine Leerzeilen. Gestaffelt wird
    /// ab dem ersten Block mit run() zurückgestellt, eine solche Zeile als Ganzes.
    /// </summary>
    /// <param name="block">Die Nummer des letzten Blocks davor, zählt weiter.</param>
    /// <param name="form">Zeichnet Ersetzungen und die Herkunft von key() & Co. auf. Null beim Auswerten eines
    /// zurückgestellten Blocks, dessen Herkunft schon feststeht.</param>
    string Expand(string text, ref int block, bool staged, FormBuilder? form)
    {
        var blocks = SnippetScript.Block().Matches(text);
        if (blocks.Count == 0) return text;

        var result = new StringBuilder(text.Length);
        var pos = 0;
        var deferring = false;
        for (var i = 0; i < blocks.Count;)
        {
            // Blöcke mit nur Leerzeichen dazwischen, allein auf ihrer Zeile: eine Zeile. Sonst ein Block für sich.
            var last = i;
            while (last + 1 < blocks.Count && IsBlank(text, End(blocks[last]), blocks[last + 1].Index))
                last++;
            var (start, end) = KeyTags.WholeLine(text, blocks[i].Index, End(blocks[last]));
            var line = start != blocks[i].Index || end != End(blocks[last]);
            if (line)
                start = Math.Max(start, pos); // der Umbruch davor gehörte schon zur Zeile davor
            else
                (last, start, end) = (i, blocks[i].Index, End(blocks[i]));
            var unit = blocks.Skip(i).Take(last - i + 1).ToList();
            result.Append(text, pos, start - pos);

            if (staged && (deferring |= unit.Any(m => SnippetScript.Waits(m.Groups[1].Value)) || KeyTags.HasIfMarker(text, start, form!)))

            {
                // Herkunft: der ganze Block oder die ganze Zeile
                form!.BeginScript(start, end - start);
                var marker = form.Marker(new DeferredStep(block + 1, text[start..end]));
                form.EndScript();
                form.Scripts.Replaced(start, end - start, marker.Length);
                result.Append(marker);
                block += unit.Count;
            }
            else
            {
                var outputs = new List<string>(unit.Count);
                foreach (var m in unit)
                {
                    form?.BeginScript(m.Index, m.Length);
                    outputs.Add(SnippetScript.Evaluate(_engine, m.Groups[1].Value, ++block));
                    form?.EndScript();
                }

                if (line && outputs.All(o => o.Length == 0))
                    form?.Scripts.Replaced(start, end - start, 0);
                else
                {
                    var p = start;
                    for (var k = 0; k < unit.Count; k++)
                    {
                        result.Append(text, p, unit[k].Index - p).Append(outputs[k]);
                        form?.Scripts.Replaced(unit[k].Index, unit[k].Length, outputs[k].Length);
                        p = End(unit[k]);
                    }
                    result.Append(text, p, end - p);
                }
            }
            pos = end;
            i = last + 1;
        }
        return result.Append(text, pos, text.Length - pos).ToString();

        static int End(Match match) => match.Index + match.Length;

        static bool IsBlank(string text, int from, int to)
        {
            for (var i = from; i < to; i++)
                if (text[i] is not (' ' or '\t')) return false;
            return true;
        }
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
            var block = step.Block - 1;
            return KeyTags.SplitWithSources(Expand(step.Text, ref block, staged: false, form: null), form, source);
        }
        finally
        {
            form.EndDeferred();
        }
    }
}
