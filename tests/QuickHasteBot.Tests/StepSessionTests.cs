using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.Tests;

/// <summary>F10, F5, Haltepunkte, Stopp, Esc und Zeitlimit des Schritt-für-Schritt-Laufs, ohne Fenster und Tasten.</summary>
public class StepSessionTests
{
    static readonly TimeSpan Short = TimeSpan.FromSeconds(5);

    /// <summary>Länger wartet kein Test, ein Fehler soll scheitern statt hängen.</summary>
    static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    /// <summary>Schritte an den Stellen 0, 10, 20 …, der Haltepunkt einer Stelle ist ihre "Zeile" (Stelle / 10).</summary>
    static IReadOnlyList<SourcedStep> Steps(int count) =>
        Enumerable.Range(0, count).Select(i => new SourcedStep(new WaitStep(0), new SourceSpan(i * 10, 5))).ToList();

    static StepSession Session(int count, int[]? breakpointLines = null, Func<bool>? escape = null, TimeSpan? timeout = null)
    {
        var lines = breakpointLines ?? [];
        var session = new StepSession(s => lines.Contains(s.Start / 10) ? s.Start / 10 : null, escape ?? (() => false), timeout ?? Short);
        session.Planned(Steps(count), paste: false);
        return session;
    }

    /// <summary>Wartet, bis der Lauf vor einem Schritt pausiert.</summary>
    static async Task Paused(StepSession session, int step)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (!(session.IsPaused && session.Current == step))
        {
            Assert.True(Environment.TickCount64 < deadline, $"keine Pause vor Schritt {step}, steht bei {session.Current}");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task It_pauses_before_the_first_step_and_F10_runs_one_step()
    {
        var session = Session(3);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        Assert.False(first.IsCompleted);

        Assert.True(session.Next());
        Assert.Equal(StepDecision.RunAfterPause, await first);
        Assert.False(session.IsPaused);

        // Nach F10 hält der nächste Schritt wieder an
        var second = session.BeforeStepAsync(1);
        await Paused(session, 1);
        Assert.True(session.Next());
        Assert.Equal(StepDecision.RunAfterPause, await second);
    }

    [Fact]
    public async Task F5_runs_to_the_next_breakpoint()
    {
        var session = Session(5, breakpointLines: [3]);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        session.Continue();
        Assert.Equal(StepDecision.RunAfterPause, await first);

        Assert.Equal(StepDecision.Run, await session.BeforeStepAsync(1));
        Assert.Equal(StepDecision.Run, await session.BeforeStepAsync(2));
        var atBreakpoint = session.BeforeStepAsync(3);
        await Paused(session, 3);
        session.Continue();
        Assert.Equal(StepDecision.RunAfterPause, await atBreakpoint);
        Assert.Equal(StepDecision.Run, await session.BeforeStepAsync(4));
    }

    [Fact]
    public async Task A_breakpoint_hit_by_several_steps_in_a_row_pauses_once()
    {
        // Schritte 1 und 2 stehen in derselben "Zeile", z.B. {tab}{tab} oder key() im selben Skriptblock
        var lines = new[] { 0, 1, 1, 2 };
        var steps = lines.Select((line, i) => new SourcedStep(new WaitStep(0), new SourceSpan(line * 10 + i, 1))).ToList();
        var session = new StepSession(s => s.Start / 10 == 1 ? 1 : null, () => false, Short);
        session.Planned(steps, paste: false);

        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        session.Continue();
        await first;

        var hit = session.BeforeStepAsync(1);
        await Paused(session, 1);
        session.Continue();
        await hit;
        Assert.Equal(StepDecision.Run, await session.BeforeStepAsync(2));
        Assert.Equal(StepDecision.Run, await session.BeforeStepAsync(3));
    }

    [Fact]
    public async Task F10_after_F5_pauses_at_every_step_again()
    {
        var session = Session(4, breakpointLines: [1]);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        session.Continue();
        await first;

        var hit = session.BeforeStepAsync(1);
        await Paused(session, 1);
        session.Next();
        await hit;

        var next = session.BeforeStepAsync(2);
        await Paused(session, 2);
        session.RequestStop();
        Assert.Equal(StepDecision.Stop, await next);
    }

    [Fact]
    public async Task Stop_while_paused_ends_the_run()
    {
        var session = Session(2);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        session.RequestStop();
        Assert.Equal(StepDecision.Stop, await first.WaitAsync(Bounded));
        Assert.Equal(StepStop.Stopped, session.Stop);
        Assert.False(session.IsPaused);
    }

    [Fact]
    public async Task Stop_while_running_ends_before_the_next_step()
    {
        var session = Session(3);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        session.Continue();
        await first;

        session.RequestStop();
        Assert.Equal(StepDecision.Stop, await session.BeforeStepAsync(1));
        Assert.Equal(StepStop.Stopped, session.Stop);
    }

    [Fact]
    public async Task Holding_escape_during_a_pause_stops()
    {
        var escape = false;
        var session = Session(2, escape: () => escape);
        var first = session.BeforeStepAsync(0);
        await Paused(session, 0);
        escape = true;
        Assert.Equal(StepDecision.Stop, await first.WaitAsync(Bounded));
        Assert.Equal(StepStop.Escape, session.Stop);
    }

    [Fact]
    public async Task A_pause_without_input_times_out()
    {
        var session = Session(2, timeout: TimeSpan.FromMilliseconds(150));
        Assert.Equal(StepDecision.Stop, await session.BeforeStepAsync(0).WaitAsync(Bounded));
        Assert.Equal(StepStop.TimedOut, session.Stop);
    }

    [Fact]
    public void The_idle_timeout_is_five_minutes() => Assert.Equal(TimeSpan.FromMinutes(5), StepSession.IdleTimeout);

    [Fact]
    public void Keys_do_nothing_outside_a_pause()
    {
        var session = Session(2);
        Assert.False(session.Next());
        Assert.False(session.Continue());
    }

    [Fact]
    public async Task It_drives_a_real_run_step_by_step()
    {
        var steps = TestSteps.Sourced(new WaitStep(1), new DelayStep(5), new GuardStep(false));
        var session = new StepSession(_ => null, () => false, Short);

        var run = new PasteService().RunFormAsync(steps, 0, windowGuard: false, session);
        await Paused(session, 0);
        session.Next();
        await Paused(session, 1);
        session.Continue();
        var result = await run;

        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal(2, session.Current);
    }
}
