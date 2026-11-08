using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Bringt Text ins Zielfenster: per Clipboard + Strg+V oder als Unicode-Tastatureingabe.
/// Ersetzt SendKeys der alten Version (Probleme mit Umlauten, Tastaturlayout und Sonderzeichen wie {}+^%~()).
/// Muss auf dem UI-Thread laufen (Clipboard braucht STA).
/// </summary>
internal enum FormOutcome
{
    Completed, EscapePressed, WindowChanged, ElementNotFound, ElementFailed, WindowNotFound, WindowNotActivated, Elevated,
    /// <summary>{start,…}: Windows konnte das Programm nicht starten (nicht gefunden, UAC abgelehnt …).</summary>
    StartFailed,
    /// <summary>{start,…}: gestartet, aber in <see cref="WindowFinder.StartTimeout"/> kein Fenster mit dem Programmnamen.</summary>
    StartedWindowNotFound,
    /// <summary>Ein zurückgestellter Skriptblock (run()) ist fehlgeschlagen, die fertige Meldung steht in Error.</summary>
    ScriptFailed,
}

/// <summary>
/// Was ein zurückgestellter Skriptblock (<see cref="DeferredStep"/>) ergeben hat: seine Schritte, oder warum nicht.
/// </summary>
/// <param name="Steps">Die Ausgabe des Blocks als Schritte, mit Herkunft für den Schritt-für-Schritt-Lauf.</param>
/// <param name="Error">Die Meldung für den Benutzer, wenn der Block fehlgeschlagen ist.</param>
/// <param name="Cancelled">Esc beim Warten auf run().</param>
internal sealed record Expansion(IReadOnlyList<SourcedStep> Steps, string? Error = null, bool Cancelled = false);

/// <param name="Step">Bei Element-Fehlern der Schritt als Tag, z.B. "{focus,#email}".</param>
/// <param name="Error">Bei <see cref="FormOutcome.ElementFailed"/> und <see cref="FormOutcome.StartFailed"/>: warum.</param>
/// <param name="StepIndex">Bei einem Abbruch der Schritt (ab 0), bei dem es passiert ist.</param>
internal sealed record FormResult(FormOutcome Outcome, string? Step = null, string? Error = null, int? StepIndex = null);

internal sealed class PasteService
{
    /// <summary>So lange bekommt das Zielprogramm Zeit, das Clipboard zu lesen, bevor wir es zurücksetzen.</summary>
    const int ClipboardRestoreDelay = 600;
    const int ModifierReleaseTimeout = 1500;
    const int TypingChunkSize = 200;

    /// <summary>Pause nach jeder Taste in Formularen, damit JavaScript (Autocomplete, Validierung) mitkommt. {delay,ms} ändert sie.</summary>
    const int DefaultKeyDelay = 30;
    const int PollInterval = 15;

    // Clipboard-Verlauf (Win+V) und Sync-Tools sollen unsere temporären Inhalte ignorieren
    const string ExcludeFromHistoryFormat = "ExcludeClipboardContentFromMonitorProcessing";
    const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    static readonly string[] PreservedFormats =
    [
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html, DataFormats.Bitmap, DataFormats.FileDrop,
    ];

    /// <returns>False, wenn das Zielprogramm mit Administratorrechten läuft und Windows die Eingabe verwerfen würde.</returns>
    public async Task<bool> PasteAsync(string text, PasteMode mode, nint targetWindow)
    {
        if (!await WindowFocus.ActivateAndWaitAsync(targetWindow))
            Log.Info("Zielfenster konnte nicht aktiviert werden, sende trotzdem an das aktuelle Vordergrundfenster");
        if (ProcessAccess.IsBlocked(GetForegroundWindow()))
        {
            Log.Info($"Zielprogramm läuft mit Administratorrechten, Windows lässt nichts einfügen: {ProcessAccess.ProcessName(GetForegroundWindow())}");
            return false;
        }

        // Sonst wird aus Strg+V bei gedrückter Shift-Taste (Shift+Enter) ein Strg+Shift+V
        await WaitForModifiersReleasedAsync();

        if (mode == PasteMode.Clipboard)
            await PasteViaClipboardAsync(text);
        else
            await TypeAsync(text);
        return true;
    }

    static async Task PasteViaClipboardAsync(string text)
    {
        var backup = SnapshotClipboard();

        var data = new DataObject();
        data.SetText(NormalizeLineEndings(text), TextDataFormat.UnicodeText);
        data.SetData(ExcludeFromHistoryFormat, new MemoryStream([0, 0, 0, 0]));
        data.SetData(CanIncludeInHistoryFormat, new MemoryStream([0, 0, 0, 0]));

        if (!TrySetClipboard(data))
        {
            Log.Error("Clipboard ist blockiert, tippe stattdessen");
            await TypeAsync(text);
            return;
        }

        Send([Key(VK_CONTROL, down: true), Key(VK_V, down: true), Key(VK_V, down: false), Key(VK_CONTROL, down: false)]);

        await Task.Delay(ClipboardRestoreDelay);
        if (backup is not null)
            TrySetClipboard(backup);
    }

    static async Task TypeAsync(string text, bool shiftEnter = false)
    {
        var inputs = CharInputs(text, shiftEnter).SelectMany(c => c).ToList();

        // In Häppchen senden, manche Programme verschlucken sonst Zeichen
        for (var i = 0; i < inputs.Count; i += TypingChunkSize)
        {
            Send(inputs.Skip(i).Take(TypingChunkSize).ToArray());
            await Task.Delay(10);
        }
    }

    /// <summary>Die Eingaben pro Zeichen. Ein Umbruch wird zu Enter, in Formularen zu Shift+Enter (neue Zeile statt Absenden).</summary>
    static IEnumerable<INPUT[]> CharInputs(string text, bool shiftEnter)
    {
        foreach (var c in text.ReplaceLineEndings("\n"))
        {
            if (c != '\n')
                yield return [Unicode(c, down: true), Unicode(c, down: false)];
            else if (shiftEnter)
                yield return [Key(VK_SHIFT, down: true), Key(VK_RETURN, down: true), Key(VK_RETURN, down: false), Key(VK_SHIFT, down: false)];
            else
                yield return [Key(VK_RETURN, down: true), Key(VK_RETURN, down: false)];
        }
    }

    /// <summary>
    /// Der Schritt, mit dem ein Baustein sein Fenster selbst nach vorne holt ({window,…}, {start,…} oder ein Element-Tag
    /// mit Fenster), falls er so beginnt. Pausen und {guard} davor zählen nicht. Sonst null.
    /// </summary>
    internal static FormStep? StartWindowStep(IEnumerable<FormStep> steps) =>
        steps.FirstOrDefault(s => s is not (DelayStep or GuardStep)) is { } first
        && first is WindowStep or StartStep or ElementStep { Selector.Window: not null } ? first : null;

    /// <summary>
    /// Führt einen Formular-Baustein aus (Text und Tasten-Tags, siehe <see cref="KeyTags"/>). Immer getippt, damit Text
    /// und Tasten in derselben Eingabe-Warteschlange und damit in der richtigen Reihenfolge ankommen.
    /// Bricht ab, wenn die echte Esc-Taste gedrückt ist oder (mit Fensterschutz) ein anderes Fenster in den Vordergrund kommt.
    /// Der Lauf hält die einzige Schrittliste: Er meldet sie dem <paramref name="gate"/> und erweitert sie um die Schritte
    /// zurückgestellter Blöcke.
    /// </summary>
    /// <param name="gate">Darf vor jedem Schritt anhalten (Schritt für Schritt). Nach einer Pause kommt das Fenster, das
    /// vorher vorne war, zurück nach vorne, sonst bricht der Lauf ab.</param>
    /// <param name="expand">
    /// Wertet einen zurückgestellten Skriptblock aus, wenn der Lauf dort ankommt (run() an seiner Stelle), mit der Herkunft
    /// des Blocks. Seine Schritte kommen direkt dahinter. Ohne werden solche Blöcke übersprungen.
    /// </param>
    public async Task<FormResult> RunFormAsync(IReadOnlyList<SourcedStep> plannedSteps, nint targetWindow, bool windowGuard, IStepGate? gate = null,
        Func<DeferredStep, SourceSpan, Task<Expansion>>? expand = null)
    {
        var steps = plannedSteps.ToList();
        gate?.Planned([.. steps], paste: false);
        // Beginnt der Baustein mit {window,…} oder {start,…}, ist das Fenster vom Hotkey egal
        var startsWithWindow = StartWindowStep(steps.Select(s => s.Step)) is not null;
        if (!startsWithWindow && !await WindowFocus.ActivateAndWaitAsync(targetWindow))
        {
            // Anders als beim Einfügen nicht einfach ans aktuelle Fenster senden, {enter} & Co. könnten dort Schaden anrichten
            if (windowGuard)
                return Aborted(FormOutcome.WindowChanged, 0);
            Log.Info("Zielfenster konnte nicht aktiviert werden, sende trotzdem an das aktuelle Vordergrundfenster (Fensterschutz aus)");
        }
        await WaitForModifiersReleasedAsync();

        var guarded = windowGuard;
        var guardOff = false; // {guard,off} im Baustein: auch {window,…} schaltet den Schutz nicht wieder ein
        var guardWindow = RootOwner(GetForegroundWindow());
        var keyDelay = DefaultKeyDelay;
        var charDelay = 0;
        nint checkedWindow = 0;
        var checkedBlocked = false;

        for (var i = 0; i < steps.Count; i++)
        {
            if (gate is not null)
            {
                var front = GetForegroundWindow();
                switch (await gate.BeforeStepAsync(i))
                {
                    case StepDecision.Stop:
                        return Aborted(FormOutcome.EscapePressed, i);
                    case StepDecision.RunAfterPause when !await ResumeAsync(front):
                        return Aborted(FormOutcome.WindowChanged, i);
                }
            }
            if (EscapeDown())
                return Aborted(FormOutcome.EscapePressed, i);
            if (guarded && WindowChanged(ref guardWindow))
                return Aborted(FormOutcome.WindowChanged, i);

            // {window,…} vor dem Element: Programm nach vorne holen, dann dort suchen. {start,…} startet es vorher, falls nötig.
            var step = steps[i].Step;
            var searchWindow = GetForegroundWindow();
            var window = step switch
            {
                WindowStep w => w.Window,
                StartStep s => s.Window,
                ElementStep { Selector.Window: { } w } => w,
                _ => null,
            };
            if (window is not null)
            {
                var (hwnd, failure) = await BringToFrontAsync(window, i, step as StartStep);
                if (failure is not null) return failure;
                searchWindow = hwnd;
                guardWindow = RootOwner(hwnd);
                guarded = windowGuard && !guardOff;
            }

            // Programme mit Administratorrechten nehmen von uns nichts an (UIPI), ohne dass Windows es meldet
            if (step is TextStep or KeyStep or ElementStep && Blocked(searchWindow))
                return Aborted(FormOutcome.Elevated, i, ProcessAccess.ProcessName(searchWindow));

            switch (step)
            {
                case TextStep text when charDelay == 0:
                    await TypeAsync(text.Text, shiftEnter: true);
                    break;

                case TextStep text:
                    foreach (var c in CharInputs(text.Text, shiftEnter: true))
                    {
                        if (EscapeDown())
                            return Aborted(FormOutcome.EscapePressed, i);
                        Send(c);
                        await Task.Delay(charDelay);
                    }
                    break;

                case KeyStep key:
                    for (var r = 0; r < key.Repeat; r++)
                    {
                        Send(KeyPress(key));
                        if (keyDelay > 0)
                            await Task.Delay(keyDelay);
                    }
                    // Unser eigenes Esc soll nicht als Notbremse zählen: abwarten, bis es sicher losgelassen ist
                    if (key.Vk == VK_ESCAPE)
                        await Task.Delay(PollInterval * 2);
                    if (key.SwitchesWindow)
                        guarded = false;
                    break;

                case WaitStep wait:
                    var until = Environment.TickCount64 + wait.Milliseconds;
                    while (Environment.TickCount64 < until)
                    {
                        if (EscapeDown())
                            return Aborted(FormOutcome.EscapePressed, i);
                        await Task.Delay((int)Math.Clamp(until - Environment.TickCount64, 1, PollInterval));
                    }
                    break;

                case DelayStep delay:
                    keyDelay = charDelay = delay.Milliseconds;
                    break;

                case WindowStep or StartStep:
                    // Schon oben nach vorne geholt, kurz Zeit lassen, bis das Programm Eingaben annimmt
                    await Task.Delay(Math.Max(keyDelay, 100));
                    break;

                case ElementStep element:
                {
                    // UI Automation blockiert, darum nicht auf dem UI-Thread. Gesucht wird im Fenster, das jetzt vorne ist.
                    var result = await Task.Run(() => ElementAutomation.Run(element, searchWindow, EscapeDown));
                    switch (result.Outcome)
                    {
                        case ElementOutcome.Cancelled:
                            return Aborted(FormOutcome.EscapePressed, i);
                        case ElementOutcome.NotFound:
                            return Aborted(FormOutcome.ElementNotFound, i);
                        case ElementOutcome.Failed:
                            return Aborted(FormOutcome.ElementFailed, i, result.Error);
                        case ElementOutcome.TypeInstead:
                            Send(KeyPress(ElementAutomation.SelectAll));
                            await Task.Delay(Math.Max(keyDelay, PollInterval));
                            await TypeAsync(result.Text ?? "", shiftEnter: true);
                            break;
                    }
                    if (keyDelay > 0)
                        await Task.Delay(keyDelay);
                    break;
                }

                case DeferredStep deferred when expand is not null:
                {
                    // Das Fenster soll das bisher Getippte verarbeitet haben, bevor ein Skriptfenster ihm den Fokus nimmt.
                    // Sonst tippt es verspätet, und alle Zeichen kommen als das letzte an.
                    var front = GetForegroundWindow();
                    await SettleAsync(front, keyDelay);
                    var expansion = await expand(deferred, steps[i].Source);
                    if (expansion.Cancelled)
                        return Aborted(FormOutcome.EscapePressed, i);
                    if (expansion.Error is not null)
                        return Aborted(FormOutcome.ScriptFailed, i, expansion.Error);
                    steps.InsertRange(i + 1, expansion.Steps);
                    gate?.Planned([.. steps], paste: false);
                    if (GetForegroundWindow() != front)
                    {
                        // Ein sichtbares Konsolenfenster kam nach vorne: warten, bis es sich geschlossen hat, dann das
                        // Fenster von vorher zurückholen und ihm Zeit lassen, bevor weitergetippt wird
                        await ForegroundSettledAsync();
                        if (!await ResumeAsync(front))
                            return Aborted(FormOutcome.WindowChanged, i);
                        await SettleAsync(front, keyDelay);
                    }
                    break;
                }

                case GuardStep guard:
                    guarded = guard.On;
                    guardOff = !guard.On;
                    if (guard.On)
                        guardWindow = RootOwner(GetForegroundWindow());
                    break;
            }
        }
        return new FormResult(FormOutcome.Completed);

        bool Blocked(nint hwnd)
        {
            if (hwnd != checkedWindow)
                (checkedWindow, checkedBlocked) = (hwnd, ProcessAccess.IsBlocked(hwnd));
            return checkedBlocked;
        }

        // Fenster suchen (bis FindTimeout, das Programm startet vielleicht gerade), wiederherstellen und nach vorne holen.
        // Mit start: ist keins da, das Programm starten und länger warten, bis StartTimeout.
        async Task<(nint Window, FormResult? Failure)> BringToFrontAsync(WindowSelector selector, int step, StartStep? start)
        {
            var hwnd = await Task.Run(() => WindowFinder.Find(selector));
            var launched = false;
            Process? process = null;
            try
            {
                if (hwnd == 0 && start is not null)
                {
                    if (!WindowFinder.TryStart(start, out process, out var error))
                        return (0, Aborted(FormOutcome.StartFailed, step, error));
                    launched = true;
                }

                var timeout = launched ? WindowFinder.StartTimeout : ElementAutomation.FindTimeout;
                var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
                while (hwnd == 0)
                {
                    if (EscapeDown()) return (0, Aborted(FormOutcome.EscapePressed, step));
                    if (Environment.TickCount64 > deadline)
                        return (0, Aborted(launched ? FormOutcome.StartedWindowNotFound : FormOutcome.WindowNotFound, step));
                    await Task.Delay(200);
                    hwnd = await Task.Run(() => WindowFinder.Find(selector));
                }
                if (ProcessAccess.IsBlocked(hwnd))
                    return (0, Aborted(FormOutcome.Elevated, step, ProcessAccess.ProcessName(hwnd)));
                // Das Fenster ist da, das Programm lädt aber vielleicht noch
                if (process is not null && !await WindowFinder.WaitForInputIdleAsync(process, EscapeDown))
                    return (0, Aborted(FormOutcome.EscapePressed, step));
                if (!await WindowFinder.ActivateAsync(hwnd))
                    return (0, Aborted(FormOutcome.WindowNotActivated, step));
                return (hwnd, null);
            }
            finally
            {
                process?.Dispose();
            }
        }

        FormResult Aborted(FormOutcome outcome, int step, string? error = null)
        {
            Log.Info($"Formular-Baustein abgebrochen ({outcome}) bei Schritt {step + 1} von {steps.Count}");
            return new FormResult(outcome, step < steps.Count ? KeyTags.Describe(steps[step].Step) : null, error, step);
        }
    }

    /// <summary>
    /// Nach einer Pause im Schritt-für-Schritt-Lauf: das Fenster, das vorher vorne war, zurückholen. Inzwischen war
    /// vielleicht der Editor vorne. Dann wie beim Start warten, bis keine Zusatztaste mehr gedrückt ist.
    /// </summary>
    /// <summary>
    /// Wartet, bis <paramref name="window"/> wieder Nachrichten abarbeitet (höchstens 1 s), und gibt ihm dann wie nach
    /// {window,…} kurz Zeit für die Eingaben in seiner Warteschlange.
    /// </summary>
    static async Task SettleAsync(nint window, int keyDelay)
    {
        if (window != 0)
            await Task.Run(() => SendMessageTimeout(window, WM_NULL, 0, 0, SMTO_ABORTIFHUNG, 1000, out _));
        await Task.Delay(Math.Max(keyDelay, 100));
    }

    /// <summary>Wartet, bis das Vordergrundfenster 200 ms lang dasselbe bleibt, höchstens 1,5 s.</summary>
    static async Task ForegroundSettledAsync()
    {
        var deadline = Environment.TickCount64 + 1500;
        var window = GetForegroundWindow();
        var since = Environment.TickCount64;
        while (Environment.TickCount64 < deadline && Environment.TickCount64 - since < 200)
        {
            await Task.Delay(PollInterval);
            if (GetForegroundWindow() is var now && now != window)
                (window, since) = (now, Environment.TickCount64);
        }
    }

    internal static async Task<bool> ResumeAsync(nint window)
    {
        if (window != 0 && GetForegroundWindow() != window && !await WindowFocus.ActivateAndWaitAsync(window))
        {
            Log.Info("Zielfenster nach der Pause nicht zurückgeholt, Schritt-für-Schritt-Lauf abgebrochen");
            return false;
        }
        await WaitForModifiersReleasedAsync();
        return true;
    }

    static INPUT[] KeyPress(KeyStep key)
    {
        var inputs = new List<INPUT>();
        foreach (var modifier in key.Modifiers)
            inputs.Add(Key(modifier, down: true));
        inputs.Add(Key(key.Vk, down: true));
        inputs.Add(Key(key.Vk, down: false));
        for (var m = key.Modifiers.Count - 1; m >= 0; m--)
            inputs.Add(Key(key.Modifiers[m], down: false));
        return inputs.ToArray();
    }

    internal static bool EscapeDown() => (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;

    /// <summary>
    /// Ein anderes Programm ist vorne. Dialoge des Zielfensters zählen nicht als Wechsel (gleicher Besitzer),
    /// ein kurzzeitig leeres Vordergrundfenster während eines Wechsels auch nicht.
    /// </summary>
    static bool WindowChanged(ref nint guardWindow)
    {
        var current = RootOwner(GetForegroundWindow());
        if (current == 0) return false;
        if (guardWindow == 0)
        {
            guardWindow = current;
            return false;
        }
        return current != guardWindow;
    }

    static nint RootOwner(nint hwnd)
    {
        if (hwnd == 0) return 0;
        var root = GetAncestor(hwnd, GA_ROOTOWNER);
        return root != 0 ? root : hwnd;
    }

    static async Task WaitForModifiersReleasedAsync()
    {
        var deadline = Environment.TickCount64 + ModifierReleaseTimeout;
        while (AnyModifierDown() && Environment.TickCount64 < deadline)
            await Task.Delay(15);
    }

    static bool AnyModifierDown() =>
        new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>Kopiert die gängigen Formate, damit wir sie nach dem Einfügen zurückschreiben können.</summary>
    static DataObject? SnapshotClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null) return null;

            var copy = new DataObject();
            var any = false;
            foreach (var format in PreservedFormats)
            {
                if (!current.GetDataPresent(format, autoConvert: false)) continue;
                var value = current.GetData(format, autoConvert: false);
                if (value is null) continue;
                copy.SetData(format, value);
                any = true;
            }
            return any ? copy : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Error("Clipboard konnte nicht gesichert werden", ex);
            return null;
        }
    }

    /// <summary>Text aus der Zwischenablage für Skripte, leer wenn keiner da ist oder sie blockiert ist.</summary>
    public static string TryGetClipboardText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(50);
            }
        }
        Log.Error("Clipboard ist blockiert, Skripte sehen eine leere Zwischenablage");
        return "";
    }

    static bool TrySetClipboard(IDataObject data)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\r\n");

    static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Log.Error($"SendInput hat nur {sent} von {inputs.Length} Eingaben gesendet (Win32-Fehler {Marshal.GetLastPInvokeError()})");
    }

    static INPUT Key(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = (down ? 0 : KEYEVENTF_KEYUP) | (KeyTags.IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0) },
        },
    };

    static INPUT Unicode(char c, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP) } },
    };
}
