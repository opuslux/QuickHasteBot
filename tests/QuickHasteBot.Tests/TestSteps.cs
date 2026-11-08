using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

static class TestSteps
{
    /// <summary>Schritte mit Herkunft für den Lauf: Schritt i kommt aus Zeichen i.</summary>
    public static SourcedStep[] Sourced(params FormStep[] steps) =>
        [.. steps.Select((step, i) => new SourcedStep(step, new SourceSpan(i, 1)))];
}
