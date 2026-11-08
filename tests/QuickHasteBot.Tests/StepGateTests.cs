using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.Tests;

/// <summary>
/// Der Haltepunkt-Aufruf vor jedem Schritt. Nur mit Schritten, die nichts senden ({wait}, {delay}, {guard,off}),
/// und ohne Zielfenster (0), damit die Tests nie in ein echtes Fenster tippen.
/// </summary>
public class StepGateTests
{
    static readonly SourcedStep[] Quiet = TestSteps.Sourced(new WaitStep(1), new DelayStep(5), new GuardStep(false));

    internal sealed class RecordingGate(int stopAt = -1) : IStepGate
    {
        public List<int> Seen { get; } = [];
        public List<SourcedStep>? Steps { get; private set; }
        public bool? Paste { get; private set; }
        public bool PlannedBeforeFirstStep { get; private set; }

        public void Planned(IReadOnlyList<SourcedStep> steps, bool paste) => (Steps, Paste) = (steps.ToList(), paste);

        public Task<StepDecision> BeforeStepAsync(int step)
        {
            if (Seen.Count == 0)
                PlannedBeforeFirstStep = Steps is not null;
            Seen.Add(step);
            return Task.FromResult(step == stopAt ? StepDecision.Stop : StepDecision.Run);
        }
    }

    [Fact]
    public async Task The_gate_is_asked_before_every_step_in_order()
    {
        var gate = new RecordingGate();
        var result = await new PasteService().RunFormAsync(Quiet, 0, windowGuard: false, gate);
        Assert.Equal(FormOutcome.Completed, result.Outcome);
        Assert.Equal([0, 1, 2], gate.Seen);
    }

    [Fact]
    public async Task The_run_hands_its_steps_to_the_gate_first()
    {
        var gate = new RecordingGate();
        await new PasteService().RunFormAsync(Quiet, 0, windowGuard: false, gate);
        Assert.True(gate.PlannedBeforeFirstStep);
        Assert.Equal(Quiet, gate.Steps);
        Assert.False(gate.Paste);
    }

    [Fact]
    public async Task Stop_ends_the_run_before_that_step()
    {
        var gate = new RecordingGate(stopAt: 1);
        var result = await new PasteService().RunFormAsync(Quiet, 0, windowGuard: false, gate);
        Assert.Equal(FormOutcome.EscapePressed, result.Outcome);
        Assert.Equal(1, result.StepIndex);
        Assert.Equal("{delay,5}", result.Step);
        Assert.Equal([0, 1], gate.Seen);
    }

    [Fact]
    public async Task Without_a_gate_the_run_is_the_same()
    {
        var result = await new PasteService().RunFormAsync(Quiet, 0, windowGuard: false);
        Assert.Equal(new FormResult(FormOutcome.Completed), result);
    }

    [Fact]
    public async Task A_snippet_hands_its_steps_with_their_place_to_the_gate()
    {
        using var dir = new TempDirectory();
        using var app = new AppController(new AppPaths(dir.Path));
        app.Settings.KeyTagsWindowGuard = false;

        const string text = "{wait,1}{{ key('delay,5') }}{guard,off}";
        var gate = new RecordingGate(stopAt: 2);
        var problem = await app.RunTextAsync(text, "Test", alternate: false, targetWindow: 0, gate: gate);

        Assert.False(gate.Paste);
        Assert.Equal(["{wait,1}", "{{ key('delay,5') }}", "{guard,off}"],
            gate.Steps!.Select(s => text.Substring(s.Source.Start, s.Source.Length)));
        Assert.Equal([0, 1, 2], gate.Seen);
        Assert.NotNull(problem);
        Assert.True(problem.Stopped);
        Assert.Equal(2, problem.Step);
    }

    [Fact]
    public async Task A_snippet_without_tags_is_one_paste_step()
    {
        using var dir = new TempDirectory();
        using var app = new AppController(new AppPaths(dir.Path));

        // Stop vor dem Einfügen: es wird nichts gesendet
        var gate = new RecordingGate(stopAt: 0);
        var problem = await app.RunTextAsync("Freundliche Grüsse\r\nMax", "Test", alternate: false, targetWindow: 0, gate: gate);

        Assert.True(gate.Paste);
        var step = Assert.Single(gate.Steps!);
        Assert.Equal(new TextStep("Freundliche Grüsse\r\nMax"), step.Step);
        Assert.Equal(new SourceSpan(0, 23), step.Source);
        Assert.Equal([0], gate.Seen);
        Assert.True(problem?.Stopped);
    }
}
