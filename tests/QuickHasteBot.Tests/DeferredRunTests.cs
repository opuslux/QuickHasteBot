using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.Tests;

/// <summary>
/// Der Lauf wertet zurückgestellte Blöcke aus, wenn er dort ankommt, und setzt ihre Schritte dahinter ein. Nur mit
/// Pausen, damit nichts getippt wird.
/// </summary>
public class DeferredRunTests
{
    static Task<FormResult> Run(IReadOnlyList<FormStep> steps, Func<DeferredStep, int, Task<Expansion>> expand) =>
        new PasteService().RunFormAsync(steps, targetWindow: 0, windowGuard: false, expand: expand);

    [Fact]
    public async Task Steps_of_a_block_run_right_after_it()
    {
        var seen = new List<(int Block, int Index)>();
        var result = await Run([new WaitStep(1), new DeferredStep(1, "a"), new WaitStep(1)], (step, index) =>
        {
            seen.Add((step.Block, index));
            // Der erste Block bringt einen weiteren zurückgestellten mit, der kommt gleich danach dran
            return Task.FromResult(step.Block == 1
                ? new Expansion([new WaitStep(1), new DeferredStep(2, "b")])
                : new Expansion([]));
        });
        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal([(1, 1), (2, 3)], seen);
    }

    [Fact]
    public async Task Failed_block_stops_with_its_message()
    {
        var result = await Run([new WaitStep(1), new DeferredStep(1, "a"), new WaitStep(1)],
            (_, _) => Task.FromResult(new Expansion([], "Block 1 kaputt")));
        Assert.Equal(FormOutcome.ScriptFailed, result.Outcome);
        Assert.Equal("Block 1 kaputt", result.Error);
        Assert.Equal(1, result.StepIndex);
    }

    [Fact]
    public async Task Escape_while_waiting_stops_like_escape()
    {
        var result = await Run([new DeferredStep(1, "a")], (_, _) => Task.FromResult(new Expansion([], Cancelled: true)));
        Assert.Equal(FormOutcome.EscapePressed, result.Outcome);
    }
}
