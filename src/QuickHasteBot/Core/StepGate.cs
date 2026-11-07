namespace QuickHasteBot.Core;

/// <summary>Was vor einem Schritt passiert, siehe <see cref="IStepGate"/>.</summary>
internal enum StepDecision
{
    /// <summary>Ohne Halt weiter.</summary>
    Run,

    /// <summary>Der Lauf hat pausiert. Der Aufrufer holt das Zielfenster zurück und bricht ab, wenn das nicht klappt.</summary>
    RunAfterPause,

    /// <summary>Abbrechen, der Schritt läuft nicht mehr.</summary>
    Stop,
}

/// <summary>
/// Lässt einen Baustein vor jedem Schritt anhalten, für den Schritt-für-Schritt-Lauf im Editor. Ohne Gate laufen
/// Bausteine wie immer.
/// </summary>
internal interface IStepGate
{
    /// <summary>
    /// Die Schritte stehen fest, Skripte sind gelaufen. Kommt vor dem ersten <see cref="BeforeStepAsync"/>.
    /// <paramref name="paste"/>: ein Baustein ohne Tasten-Tags, ein einziger Schritt, der alles einfügt.
    /// </summary>
    void Planned(IReadOnlyList<SourcedStep> steps, bool paste);

    /// <summary>Vor Schritt <paramref name="step"/> (ab 0), das Zielfenster ist vorne.</summary>
    Task<StepDecision> BeforeStepAsync(int step);
}
