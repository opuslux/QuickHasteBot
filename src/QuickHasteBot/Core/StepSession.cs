namespace QuickHasteBot.Core;

/// <summary>Warum ein Schritt-für-Schritt-Lauf von sich aus aufgehört hat.</summary>
internal enum StepStop
{
    None,

    /// <summary>Stopp gedrückt.</summary>
    Stopped,

    /// <summary>Esc während einer Pause gedrückt gehalten.</summary>
    Escape,

    /// <summary><see cref="StepSession.IdleTimeout"/> lang keine Eingabe in einer Pause.</summary>
    TimedOut,
}

/// <summary>
/// Schritt für Schritt: hält vor dem ersten Schritt an, danach vor jedem (<see cref="Next"/>, F10) oder nur an
/// Haltepunkten (<see cref="Continue"/>, F5). Läuft auf dem UI-Thread, wie der Baustein selbst.
/// </summary>
internal sealed class StepSession : IStepGate
{
    /// <summary>So lange darf eine Pause ohne Eingabe dauern, dann stoppt der Lauf.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    const int PollInterval = 50;

    readonly Func<SourceSpan, int?> _breakpoint;
    readonly Func<bool> _escapeDown;
    readonly TimeSpan _timeout;
    TaskCompletionSource<StepDecision>? _waiting;
    bool _toBreakpoint;
    bool _stopRequested;
    int? _lastBreakpoint;

    /// <param name="breakpoint">Der Haltepunkt, den ein Schritt an dieser Stelle trifft (z.B. die Zeile), sonst null.
    /// F5 hält an, wenn ein Schritt einen anderen Haltepunkt trifft als der vorige, so hält eine Zeile mit mehreren
    /// Tags nur einmal.</param>
    /// <param name="escapeDown">Ob Esc gerade gedrückt ist, in Pausen abgefragt.</param>
    public StepSession(Func<SourceSpan, int?> breakpoint, Func<bool> escapeDown, TimeSpan? timeout = null)
    {
        _breakpoint = breakpoint;
        _escapeDown = escapeDown;
        _timeout = timeout ?? IdleTimeout;
    }

    public IReadOnlyList<SourcedStep> Steps { get; private set; } = [];

    /// <summary>Ein Baustein ohne Tasten-Tags: ein einziger Schritt, der alles einfügt.</summary>
    public bool Paste { get; private set; }

    /// <summary>Der Schritt, vor dem der Lauf steht oder der gerade läuft, -1 vor dem Start.</summary>
    public int Current { get; private set; } = -1;

    public bool IsPaused => _waiting is not null;

    public StepStop Stop { get; private set; }

    /// <summary>Schritte geplant, Pause begonnen oder ein Schritt läuft.</summary>
    public event EventHandler? Changed;

    public void Planned(IReadOnlyList<SourcedStep> steps, bool paste)
    {
        (Steps, Paste) = (steps, paste);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<StepDecision> BeforeStepAsync(int step)
    {
        Current = step;
        if (_stopRequested) return Ended(StepStop.Stopped);

        var breakpoint = step < Steps.Count ? _breakpoint(Steps[step].Source) : null;
        var hitsBreakpoint = breakpoint is not null && breakpoint != _lastBreakpoint;
        _lastBreakpoint = breakpoint;
        if (step > 0 && _toBreakpoint && !hitsBreakpoint)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return StepDecision.Run;
        }

        var waiting = _waiting = new TaskCompletionSource<StepDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        Changed?.Invoke(this, EventArgs.Empty);
        var deadline = Environment.TickCount64 + (long)_timeout.TotalMilliseconds;
        var reason = StepStop.None;
        while (!waiting.Task.IsCompleted)
        {
            if (_escapeDown())
                reason = StepStop.Escape;
            else if (Environment.TickCount64 >= deadline)
                reason = StepStop.TimedOut;
            if (reason != StepStop.None) break;
            await Task.WhenAny(waiting.Task, Task.Delay(PollInterval));
        }
        _waiting = null;

        if (reason != StepStop.None) return Ended(reason);
        if (waiting.Task.Result == StepDecision.Stop) return Ended(StepStop.Stopped);
        Changed?.Invoke(this, EventArgs.Empty);
        return StepDecision.RunAfterPause;
    }

    /// <summary>F10: diesen Schritt ausführen und vor dem nächsten wieder anhalten. False, wenn gerade keine Pause ist.</summary>
    public bool Next() => Resume(toBreakpoint: false);

    /// <summary>F5: weiter bis zum nächsten Haltepunkt oder zum Ende.</summary>
    public bool Continue() => Resume(toBreakpoint: true);

    /// <summary>Stopp: sofort, wenn der Lauf pausiert, sonst vor dem nächsten Schritt.</summary>
    public void RequestStop()
    {
        _stopRequested = true;
        _waiting?.TrySetResult(StepDecision.Stop);
    }

    bool Resume(bool toBreakpoint)
    {
        if (_waiting is null) return false;
        _toBreakpoint = toBreakpoint;
        return _waiting.TrySetResult(StepDecision.RunAfterPause);
    }

    StepDecision Ended(StepStop reason)
    {
        Stop = reason;
        Changed?.Invoke(this, EventArgs.Empty);
        return StepDecision.Stop;
    }
}
