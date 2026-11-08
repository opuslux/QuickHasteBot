using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Tests.TestSteps;

namespace QuickHasteBot.Tests;

/// <summary>
/// Der Lauf wertet zurückgestellte Blöcke aus, wenn er dort ankommt, und setzt ihre Schritte dahinter ein. Nur mit
/// Pausen, damit nichts getippt wird.
/// </summary>
public class DeferredRunTests
{
    static Task<FormResult> Run(IReadOnlyList<SourcedStep> steps, Func<DeferredStep, SourceSpan, Task<Expansion>> expand,
        IStepGate? gate = null) =>
        new PasteService().RunFormAsync(steps, targetWindow: 0, windowGuard: false, gate, expand);

    /// <summary>Wie ScriptSession.Evaluate: Alle Schritte eines Blocks kommen aus dem Block.</summary>
    static Expansion From(SourceSpan source, params FormStep[] steps) =>
        new([.. steps.Select(step => new SourcedStep(step, source))]);

    [Fact]
    public async Task Steps_of_a_block_run_right_after_it()
    {
        var seen = new List<(int Block, SourceSpan Source)>();
        var result = await Run(Sourced(new WaitStep(1), new DeferredStep(1, "a"), new WaitStep(1)), (step, source) =>
        {
            seen.Add((step.Block, source));
            // Der erste Block bringt einen weiteren zurückgestellten mit, der kommt gleich danach dran
            return Task.FromResult(step.Block == 1 ? From(source, new WaitStep(1), new DeferredStep(2, "b")) : From(source));
        });
        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal([(1, new SourceSpan(1, 1)), (2, new SourceSpan(1, 1))], seen);
    }

    [Fact]
    public async Task The_gate_sees_the_added_steps_with_their_place()
    {
        var gate = new StepGateTests.RecordingGate();
        var result = await Run(Sourced(new WaitStep(1), new DeferredStep(1, "a"), new WaitStep(2)),
            (_, source) => Task.FromResult(From(source, new WaitStep(3))), gate);
        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal([0, 1, 2, 3], gate.Seen);
        Assert.Equal([new(new WaitStep(1), new(0, 1)), new(new DeferredStep(1, "a"), new(1, 1)),
            new(new WaitStep(3), new(1, 1)), new SourcedStep(new WaitStep(2), new(2, 1))], gate.Steps);
    }

    [Fact]
    public async Task Failed_block_stops_with_its_message()
    {
        var result = await Run(Sourced(new WaitStep(1), new DeferredStep(1, "a"), new WaitStep(1)),
            (_, _) => Task.FromResult(new Expansion([], "Block 1 kaputt")));
        Assert.Equal(FormOutcome.ScriptFailed, result.Outcome);
        Assert.Equal("Block 1 kaputt", result.Error);
        Assert.Equal(1, result.StepIndex);
    }

    [Fact]
    public async Task Escape_while_waiting_stops_like_escape()
    {
        var result = await Run(Sourced(new DeferredStep(1, "a")), (_, _) => Task.FromResult(new Expansion([], Cancelled: true)));
        Assert.Equal(FormOutcome.EscapePressed, result.Outcome);
    }
}
