using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>Was die Leiste unter der Aufnahme meldet.</summary>
internal enum RecordNote
{
    None,

    /// <summary>Ein Passwortfeld: nur fokussiert, der Wert bleibt draussen.</summary>
    Password,

    /// <summary>Das Programm vorne läuft als Administrator, Windows lässt uns nichts sehen.</summary>
    Elevated,

    /// <summary>Ein Element ohne id, Beschriftung oder Klasse: lässt sich nicht wiederfinden.</summary>
    Unnamed,
}

/// <summary>
/// Schaut zu, während man ein Formular selbst ausfüllt, und sammelt daraus <see cref="RecordedAction"/>s für
/// <see cref="Recording.Build"/>. Ohne Tastatur- oder Maus-Hook: Fokus und Feldwerte kommen über UI Automation (dieselben
/// Muster, die {set} zum Schreiben nutzt), Klicks über den Zustand der linken Maustaste wie beim Warten aufs Zielfeld.
/// Einzelne Tasten sieht der Recorder nicht, nur was sie bewirken: ein anderes Feld, einen neuen Wert.
/// Alle UIA-Zugriffe laufen auf einem eigenen Thread, nacheinander, damit die Reihenfolge stimmt.
/// </summary>
internal sealed class ActionRecorder : IDisposable
{
    const ushort VK_LBUTTON = 0x01;
    const int PollInterval = 30;
    const int RefreshInterval = 300;

    /// <summary>Optionen in einem aufgeklappten Dropdown: Fokus und Klicks darauf gehören zum Dropdown.</summary>
    static readonly HashSet<string> Options = ["ListItem", "MenuItem", "TreeItem", "DataItem", "List"];

    readonly Func<int, bool> _skip;
    readonly BlockingCollection<Action> _work = [];
    readonly Thread _worker;
    readonly CancellationTokenSource _stop = new();
    readonly List<RecordedAction> _actions = [];
    readonly Lock _lock = new();
    AutomationFocusChangedEventHandler? _focusHandler;
    Task? _poller;
    Field? _field;
    string? _process;
    int _pending;

    /// <summary>Das Feld, in dem man gerade ist, und wo es in der Aufnahme steht.</summary>
    sealed record Field(AutomationElement Element, int Index, RecordedField Recorded);

    /// <param name="skip">
    /// Prozesse, die nicht aufgenommen werden. Standard: QuickHasteBot selbst (Editor, Leiste). Tests nehmen umgekehrt nur
    /// ihre eigenen Fenster auf.
    /// </param>
    public ActionRecorder(Func<int, bool>? skip = null)
    {
        _skip = skip ?? (pid => pid == Environment.ProcessId);
        _worker = new Thread(Work) { IsBackground = true, Name = "Recorder" };
    }

    /// <summary>Etwas wurde aufgenommen oder ein Wert hat sich geändert. Kommt auf dem Recorder-Thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Ein anderes Fenster ist vorne (für die Leiste). Kommt auf einem Hintergrund-Thread.</summary>
    public event EventHandler<nint>? ForegroundChanged;

    /// <summary>Das Neueste, was die Leiste melden soll.</summary>
    public RecordNote Note { get; private set; }

    /// <summary>Was bisher aufgenommen ist, ein Feld mit dem Wert, den es gerade hat.</summary>
    public IReadOnlyList<RecordedAction> Snapshot()
    {
        lock (_lock)
            return [.. _actions];
    }

    /// <summary>Beginnt mit dem Feld, das gerade den Fokus hat (man hat eben ins Zielfenster geklickt).</summary>
    /// <param name="poll">Maus und Vordergrundfenster abfragen. Tests treiben nur den Fokus.</param>
    public void Start(bool poll = true)
    {
        _worker.Start();
        Enqueue(() =>
        {
            _focusHandler = (sender, _) =>
            {
                if (sender is AutomationElement element)
                    Enqueue(() => OnFocus(element));
            };
            Automation.AddAutomationFocusChangedEventHandler(_focusHandler);
            if (Safe(() => AutomationElement.FocusedElement) is { } focused)
                OnFocus(focused);
        });
        if (poll)
            _poller = Task.Run(Poll);
    }

    /// <summary>Beendet die Aufnahme: liest den Wert des letzten Felds und gibt alles zurück.</summary>
    public async Task<IReadOnlyList<RecordedAction>> StopAsync()
    {
        _stop.Cancel();
        if (_poller is not null)
            await _poller.ConfigureAwait(false);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            Unsubscribe();
            EndField();
            done.SetResult();
        });
        _work.CompleteAdding();
        await done.Task.ConfigureAwait(false);
        return Snapshot();
    }

    /// <summary>Wie ein Fokuswechsel von UI Automation (für Tests, deren Fenster nicht vorne sein muss).</summary>
    internal void Focused(AutomationElement element) => Enqueue(() => OnFocus(element));

    /// <summary>Wartet, bis alles bisher Gesehene verarbeitet ist (für Tests).</summary>
    internal Task IdleAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(done.SetResult);
        return done.Task;
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (!_work.IsAddingCompleted)
        {
            Enqueue(Unsubscribe);
            _work.CompleteAdding();
        }
    }

    void Enqueue(Action action)
    {
        try
        {
            Interlocked.Increment(ref _pending);
            _work.Add(action);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Decrement(ref _pending); // schon beendet
        }
    }

    void Work()
    {
        foreach (var action in _work.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
            {
                // Element verschwunden (Seite gewechselt, Dialog zu): dieses Ereignis auslassen
            }
            catch (Exception ex)
            {
                Log.Error("Aufnahme: Fehler beim Verarbeiten", ex);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }

    void Unsubscribe()
    {
        if (_focusHandler is null) return;
        Automation.RemoveAutomationFocusChangedEventHandler(_focusHandler);
        _focusHandler = null;
    }

    /// <summary>Linke Maustaste und Vordergrundfenster abfragen. Das Element unter der Maus beim Drücken, der Klick beim Loslassen.</summary>
    async Task Poll()
    {
        var down = false;
        ElementInfo? pressed = null;
        nint front = 0;
        var nextRefresh = Environment.TickCount64;
        while (!_stop.IsCancellationRequested)
        {
            var isDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            if (isDown && !down)
                pressed = GetCursorPos(out var point) ? ElementAutomation.Probe(point.X, point.Y) : null;
            else if (!isDown && down && pressed is { } info)
            {
                Enqueue(() => OnClick(info));
                pressed = null;
            }
            down = isDown;

            var window = GetForegroundWindow();
            if (window != front)
            {
                front = window;
                OnForeground(window);
            }

            // Den Wert des Felds mitlesen, damit er auch dann da ist, wenn das Feld beim Verlassen schon weg ist
            if (Environment.TickCount64 >= nextRefresh && Volatile.Read(ref _pending) == 0)
            {
                nextRefresh = Environment.TickCount64 + RefreshInterval;
                Enqueue(Refresh);
            }

            try
            {
                await Task.Delay(PollInterval, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    void OnForeground(nint window)
    {
        if (window == 0) return;
        GetWindowThreadProcessId(window, out var pid);
        if (_skip((int)pid)) return;
        if (ProcessAccess.IsBlocked(window))
            SetNote(RecordNote.Elevated);
        ForegroundChanged?.Invoke(this, window);
    }

    void OnFocus(AutomationElement element)
    {
        var current = element.Current;
        if (_skip(current.ProcessId)) return;
        if (_field is { } field)
        {
            if (Automation.Compare(element, field.Element)) return;
            // Die Optionen eines aufgeklappten Dropdowns: man ist noch beim Auswählen
            if (field.Recorded.Kind == FieldKind.Choice && Options.Contains(TypeName(current.ControlType))) return;
        }
        EndField();
        if (KindOf(element) is not { } kind || ElementAutomation.Describe(element) is not { } info) return;
        BeginField(element, info, kind, current.IsPassword, before: null);
    }

    void OnClick(ElementInfo info)
    {
        if (info.Blocked)
        {
            SetNote(RecordNote.Elevated);
            return;
        }
        if (info.Element is null) return;

        if (info.Type == "CheckBox")
        {
            // Der Klick schaltet um, der Wert beim Drücken ist der vorher. Kam der Fokus schon an, hat er vielleicht
            // erst nach dem Umschalten gelesen.
            if (_field is { } same && Automation.Compare(same.Element, info.Element))
            {
                if (info.Value is { } pressed && pressed != same.Recorded.Before)
                {
                    var recorded = same.Recorded with { Before = pressed };
                    lock (_lock)
                        _actions[same.Index] = recorded;
                    _field = same with { Recorded = recorded };
                }
                Refresh();
                return;
            }
            EndField();
            BeginField(info.Element, info, FieldKind.Toggle, password: false, before: info.Value);
            return;
        }
        if (Recording.DefaultAction(info.Type) != ElementAction.Click) return; // Textfeld & Co.: das macht der Fokus
        if (_field?.Recorded.Kind == FieldKind.Choice && Options.Contains(info.Type)) return; // Option im Dropdown

        EndField();
        if (ElementAutomation.BestSelector(info) is not { } selector)
        {
            SetNote(RecordNote.Unnamed);
            return;
        }
        Window(info.Process);
        Add(new RecordedClick(selector, info.Name));
    }

    void BeginField(AutomationElement element, ElementInfo info, FieldKind kind, bool password, string? before)
    {
        if (ElementAutomation.BestSelector(info) is not { } selector)
        {
            SetNote(RecordNote.Unnamed);
            return;
        }
        if (password)
            SetNote(RecordNote.Password);
        before ??= password ? null : ElementAutomation.FullValue(element);
        Window(info.Process);
        var recorded = new RecordedField(selector, kind, before, before, password);
        int index;
        lock (_lock)
        {
            index = _actions.Count;
            _actions.Add(recorded);
        }
        _field = new Field(element, index, recorded);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Den Wert beim Verlassen festhalten. Ist das Feld schon weg, bleibt der zuletzt gelesene.</summary>
    void EndField()
    {
        if (_field is null) return;
        Refresh();
        _field = null;
    }

    void Refresh()
    {
        if (_field is not { Recorded.Password: false } field) return;
        if (ElementAutomation.FullValue(field.Element) is not { } value || value == field.Recorded.After) return;
        var recorded = field.Recorded with { After = value };
        lock (_lock)
            _actions[field.Index] = recorded;
        _field = field with { Recorded = recorded };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>{window,process=…} vor dem ersten Schritt in einem Programm. Recording.Clean räumt doppelte weg.</summary>
    void Window(string process)
    {
        if (process.Length == 0 || process.Equals(_process, StringComparison.OrdinalIgnoreCase)) return;
        _process = process;
        Add(new RecordedWindow(process));
    }

    void Add(RecordedAction action)
    {
        lock (_lock)
            _actions.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void SetNote(RecordNote note)
    {
        if (Note == note) return;
        Note = note;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Textfelder, Dropdowns und Checkboxen. Ein schreibgeschütztes Feld (z.B. die ganze Webseite) zählt nicht.</summary>
    static FieldKind? KindOf(AutomationElement element)
    {
        var type = element.Current.ControlType;
        if (type == ControlType.CheckBox) return FieldKind.Toggle;
        if (type == ControlType.ComboBox) return FieldKind.Choice;
        if (type != ControlType.Edit && type != ControlType.Document) return null;
        if (element.Current.IsPassword) return FieldKind.Text;
        return element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly
            ? FieldKind.Text
            : null;
    }

    static string TypeName(ControlType type) => type.ProgrammaticName.Replace("ControlType.", "");

    static T? Safe<T>(Func<T> get) where T : class
    {
        try
        {
            return get();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
