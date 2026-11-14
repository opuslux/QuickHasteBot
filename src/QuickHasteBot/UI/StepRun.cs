using System.Windows;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>Wie der Editor einen Schritt markiert.</summary>
internal enum StepMark
{
    /// <summary>Der Lauf steht vor diesem Schritt.</summary>
    Paused,

    /// <summary>Der Schritt läuft gerade.</summary>
    Running,

    /// <summary>Hier ist der Lauf gescheitert, rot.</summary>
    Failed,

    /// <summary>Vorbei (durchgelaufen oder gestoppt), keine Markierung mehr.</summary>
    None,
}

/// <param name="Source">Wo der Schritt im Text steht, so wie er an den Lauf ging.</param>
internal sealed record StepView(int Index, SourceSpan Source, StepMark Mark);

/// <summary>
/// Ein Schritt-für-Schritt-Lauf: F10 und F5 registrieren, die Leiste am Zielfenster zeigen, vor Element-Schritten das
/// Zielfeld rahmen und am Ende das Ergebnis mit „Nochmal“ und „Schliessen“. Auf dem UI-Thread.
/// </summary>
internal sealed class StepRun(AppController app, string text, string name, nint target, Func<SourceSpan, int?> breakpoint,
    string? folder = null)
{
    StepSession? _session;
    StepBar? _bar;
    bool _cancelled;
    int _version;

    /// <summary>Für die Markierung im Editor. Ein gescheiterter Schritt bleibt markiert, auch nach „Schliessen“.</summary>
    public event EventHandler<StepView>? Changed;

    /// <summary>Von aussen beenden (Editor geschlossen oder Knopf nochmals gedrückt): stoppt und schliesst die Leiste.</summary>
    public void Cancel()
    {
        _cancelled = true;
        _session?.RequestStop();
        _bar?.Choose(again: false);
    }

    /// <summary>Bis „Schliessen“, mit jedem „Nochmal“ ein neuer Lauf mit demselben Text im selben Fenster.</summary>
    public async Task RunAsync()
    {
        using var keys = new GlobalHotkey();
        var bar = _bar = new StepBar();
        var frame = new ElementHighlight();
        keys.StepPressed += (_, id) =>
        {
            if (id == GlobalHotkey.StepNextId) _session?.Next();
            else _session?.Continue();
        };
        bar.NextClicked += (_, _) => _session?.Next();
        bar.ContinueClicked += (_, _) => _session?.Continue();
        bar.StopClicked += (_, _) => _session?.RequestStop();

        try
        {
            do
            {
                var (next, cont) = keys.RegisterStepKeys();
                var taken = (next, cont) switch
                {
                    (false, false) => "F10, F5",
                    (false, true) => "F10",
                    (true, false) => "F5",
                    _ => null,
                };
                var session = _session = new StepSession(breakpoint, EscapeDown);
                session.Changed += (_, _) => OnChanged(session, bar, frame, taken);
                Log.Info($"Schritt für Schritt: {name}");

                var problem = await app.Runner.RunTextAsync(text, name, alternate: false, target, gate: session, folder: folder);
                keys.UnregisterStepKeys();
                _version++;
                frame.Hide();
                ShowResult(session, bar, problem);
                if (_cancelled) break;
                if (!bar.IsVisible)
                    bar.ShowAt(target);
            }
            while (await bar.WaitForChoiceAsync());
        }
        finally
        {
            _session = null;
            _bar = null;
            bar.Close();
            frame.Close();
        }
    }

    void OnChanged(StepSession session, StepBar bar, ElementHighlight frame, string? taken)
    {
        var index = session.Current;
        if (index < 0 || index >= session.Steps.Count || session.Stop != StepStop.None) return;
        var step = session.Steps[index];
        var version = ++_version;

        bar.ShowStep(session.Steps, session.Paste, index, session.IsPaused);
        bar.ShowNote(taken is null ? null : string.Format(Strings.StepBarKeysTakenFormat, taken));
        var front = GetForegroundWindow();
        bar.ShowAt(front);
        Changed?.Invoke(this, new StepView(index, step.Source, session.IsPaused ? StepMark.Paused : StepMark.Running));

        frame.Hide();
        if (session.IsPaused && step.Step is ElementStep element)
            _ = FrameAsync(element, front, version, bar, frame);
        // {if,…} mit Element: zeigen, was geprüft wird. Fehlt es, steht das in der Leiste, und der Lauf nimmt den Zweig danach.
        else if (session.IsPaused && step.Step is IfStep { Condition.Element: { } selector })
            _ = FrameAsync(new ElementStep(ElementAction.Focus, selector, null), front, version, bar, frame);
    }

    /// <summary>Das Zielfeld rahmen, bevor der Schritt läuft. Im Fenster des Tags, sonst im Fenster, das vorne ist.</summary>
    async Task FrameAsync(ElementStep element, nint front, int version, StepBar bar, ElementHighlight frame)
    {
        var bounds = await Task.Run(() =>
        {
            var window = element.Selector.Window is { } selector ? WindowFinder.Find(selector) : front;
            return window == 0 || ProcessAccess.IsBlocked(window) ? null : ElementAutomation.Bounds(element.Selector, window);
        });
        if (version != _version) return; // inzwischen weiter
        if (bounds is { } rect)
            frame.ShowAt(rect);
        else
            bar.ShowNote(Strings.StepBarFieldNotFound);
    }

    void ShowResult(StepSession session, StepBar bar, SnippetRunner.RunProblem? problem)
    {
        if (problem is null)
        {
            bar.ShowResult(string.Format(Strings.StepBarDoneFormat, session.Steps.Count), null, StepResult.Done);
            Changed?.Invoke(this, new StepView(-1, default, StepMark.None));
            return;
        }
        if (problem.Stopped)
        {
            var message = session.Stop == StepStop.TimedOut
                ? string.Format(Strings.StepBarTimedOutFormat, StepSession.IdleTimeout.TotalMinutes)
                : null;
            bar.ShowResult(Strings.StepBarStopped, message, StepResult.Stopped);
            Changed?.Invoke(this, new StepView(-1, default, StepMark.None));
            return;
        }

        var failedStep = problem.Step is { } i && i < session.Steps.Count ? i : (int?)null;
        bar.ShowResult(failedStep is { } f ? string.Format(Strings.StepBarFailedFormat, f + 1) : Strings.StepBarError, problem.Message, StepResult.Failed);
        Changed?.Invoke(this, failedStep is { } s
            ? new StepView(s, session.Steps[s].Source, StepMark.Failed)
            : new StepView(-1, default, StepMark.None));
    }

    static bool EscapeDown() => (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
}
