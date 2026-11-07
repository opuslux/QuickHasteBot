using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Condition = System.Windows.Automation.Condition;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal enum ElementOutcome { Done, NotFound, Cancelled, TypeInstead, Failed }

/// <param name="Text">Bei <see cref="ElementOutcome.TypeInstead"/>: was ins fokussierte Element getippt werden soll.</param>
/// <param name="Error">Bei <see cref="ElementOutcome.Failed"/>: warum.</param>
internal sealed record ElementResult(ElementOutcome Outcome, string? Text = null, string? Error = null);

/// <summary>Was der Picker über das Element unter der Maus anzeigt.</summary>
/// <param name="Bounds">In physischen Bildschirmpixeln.</param>
/// <param name="Window">Das Hauptfenster des Programms, für "Testen" im Picker.</param>
/// <param name="Blocked">Das Programm läuft mit Administratorrechten, Windows lässt uns weder lesen noch ausfüllen.</param>
internal sealed record ElementInfo(
    AutomationElement? Element, string Id, string Name, string ClassName, string Type, string? Value, string Process, Rect Bounds,
    nint Window, bool Blocked = false);

/// <summary>
/// Elemente per UI Automation finden und bedienen: {focus,…}, {click,…}, {set,…} und der Element-Picker.
/// Browser (Chrome, Edge, Firefox) reichen ihren DOM-Baum über UIA durch: HTML-id = AutomationId, class = ClassName,
/// Beschriftung = Name. Alle Aufrufe blockieren und gehören auf einen Hintergrund-Thread, nie auf den UI-Thread.
/// </summary>
internal static class ElementAutomation
{
    /// <summary>
    /// So lange wird auf ein Element gewartet. Browser bauen ihren Baum beim ersten Zugriff erst auf, auf grossen Seiten
    /// dauert das bis 2–3 s, danach eine Suche 0,3–0,6 s.
    /// </summary>
    public static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(5);
    const int PollInterval = 150;

    static readonly Dictionary<string, ControlType> ControlTypes = ElementSelector.Types.Values.Distinct()
        .ToDictionary(n => n, n => (ControlType)typeof(ControlType).GetField(n, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!);

    /// <summary>Strg+A vor dem Eintippen, wenn sich ein Wert nicht direkt setzen lässt.</summary>
    public static readonly KeyStep SelectAll = KeyTags.TryParse("ctrl+a", null, out var steps, out _) ? (KeyStep)steps[0] : throw new InvalidOperationException();

    /// <summary>Sucht das Element im Fenster (wartet bis <see cref="FindTimeout"/>) und führt die Aktion aus.</summary>
    public static ElementResult Run(ElementStep step, nint window, Func<bool> cancelled)
    {
        var deadline = Environment.TickCount64 + (long)FindTimeout.TotalMilliseconds;
        AutomationElement? element = null;
        while (element is null)
        {
            try
            {
                element = Find(AutomationElement.FromHandle(window), step.Selector);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                // Fenster oder Baum im Umbau (Seite lädt), gleich nochmals
            }
            if (element is not null) break;
            if (cancelled()) return new(ElementOutcome.Cancelled);
            if (Environment.TickCount64 > deadline) return new(ElementOutcome.NotFound);
            Thread.Sleep(PollInterval);
        }

        try
        {
            return step.Action switch
            {
                ElementAction.Focus => Focus(element),
                ElementAction.Click => Click(element),
                _ => Set(element, step.Value ?? ""),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or ElementNotEnabledException or COMException)
        {
            Log.Error($"Element-Aktion fehlgeschlagen: {KeyTags.Describe(step)}", ex);
            return new(ElementOutcome.Failed, Error: ex.Message);
        }
    }

    /// <summary>
    /// Wo das Element gerade steht (physische Pixel), für den Rahmen im Schritt-für-Schritt-Lauf. Sucht einmal, ohne zu
    /// warten. Null, wenn es (noch) nicht da ist.
    /// </summary>
    public static Rect? Bounds(ElementSelector selector, nint window)
    {
        if (window == 0) return null;
        try
        {
            return Find(AutomationElement.FromHandle(window), selector)?.Current.BoundingRectangle is { IsEmpty: false } bounds ? bounds : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Das erste passende Element unter <paramref name="root"/>, oder das n-te bei index=.</summary>
    public static AutomationElement? Find(AutomationElement root, ElementSelector selector)
    {
        var conditions = new List<Condition>();
        if (selector.Id is not null)
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, selector.Id));
        if (selector.Name is not null)
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, selector.Name, PropertyConditionFlags.IgnoreCase));
        if (selector.ClassName is not null)
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, selector.ClassName));
        if (selector.Type is not null)
            conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlTypes[selector.Type]));
        var condition = conditions.Count switch
        {
            0 => Condition.TrueCondition,
            1 => conditions[0],
            _ => new AndCondition([.. conditions]),
        };

        if (selector.Classes.Count == 0 && selector.Index == 1)
            return root.FindFirst(TreeScope.Descendants, condition);

        // Einzelne Klassen und index= kann UIA nicht selbst: alle Treffer holen, Klassen gleich mit (ein Aufruf statt einer pro Element)
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.Full };
        cache.Add(AutomationElement.ClassNameProperty);
        using (cache.Activate())
        {
            var position = 0;
            foreach (AutomationElement candidate in root.FindAll(TreeScope.Descendants, condition))
            {
                if (selector.Classes.Count > 0)
                {
                    var classes = candidate.Cached.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (!selector.Classes.All(c => classes.Contains(c, StringComparer.Ordinal))) continue;
                }
                if (++position == selector.Index)
                    return candidate;
            }
        }
        return null;
    }

    static ElementResult Focus(AutomationElement element)
    {
        element.SetFocus();
        return new(ElementOutcome.Done);
    }

    static ElementResult Click(AutomationElement element)
    {
        if (Pattern<InvokePattern>(element, InvokePattern.Pattern) is { } invoke)
            invoke.Invoke();
        else if (Pattern<TogglePattern>(element, TogglePattern.Pattern) is { } toggle)
            toggle.Toggle();
        else if (Pattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern) is { } item)
            item.Select();
        else if (Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern) is { } expand)
        {
            if (expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) expand.Expand();
            else expand.Collapse();
        }
        else if (element.TryGetClickablePoint(out var point))
            MouseClick(point); // z.B. ein <div> mit onclick: nur ein echter Klick hilft
        else
            return new(ElementOutcome.Failed, Error: "the element can't be clicked");
        return new(ElementOutcome.Done);
    }

    static ElementResult Set(AutomationElement element, string value)
    {
        // Checkbox oder Schalter: on/off statt umschalten, damit der Baustein immer zum selben Zustand führt
        if (Pattern<TogglePattern>(element, TogglePattern.Pattern) is { } toggle && OnOff(value) is { } on)
        {
            // Chromium meldet den neuen Zustand verzögert: nach dem Umschalten darauf warten, sonst schaltet man doppelt.
            // Zweimal nur bei dreistufigen Checkboxen (Unbestimmt → An).
            for (var i = 0; i < 2 && (toggle.Current.ToggleState == ToggleState.On) != on; i++)
            {
                var before = toggle.Current.ToggleState;
                toggle.Toggle();
                var deadline = Environment.TickCount64 + 600;
                while (toggle.Current.ToggleState == before && Environment.TickCount64 < deadline)
                    Thread.Sleep(30);
            }
            return new(ElementOutcome.Done);
        }

        var type = element.Current.ControlType;
        if (type == ControlType.ComboBox || type == ControlType.List)
        {
            if (SelectOption(element, value))
                return new(ElementOutcome.Done);
            // Firefox lässt keine Option direkt wählen, Autocomplete-Felder haben keine: eintippen wählt bzw. sucht
            element.SetFocus();
            return new(ElementOutcome.TypeInstead, value);
        }

        if (Pattern<ValuePattern>(element, ValuePattern.Pattern) is { Current.IsReadOnly: false } edit)
        {
            edit.SetValue(value);
            return new(ElementOutcome.Done);
        }

        element.SetFocus();
        return new(ElementOutcome.TypeInstead, value);
    }

    /// <summary>
    /// Option mit diesem Namen auswählen. Erst ohne Aufklappen (Browser haben die Optionen auch zugeklappt im Baum),
    /// nur wenn sie fehlt aufklappen. Firefox lässt nicht direkt auswählen: dann false, und es wird eingetippt,
    /// was die Option im fokussierten Dropdown wählt. Ein offenes Dropdown würde das Eintippen schlucken.
    /// </summary>
    static bool SelectOption(AutomationElement box, string value)
    {
        if (TrySelect(box, value) is { } selected)
            return selected;

        var expand = Pattern<ExpandCollapsePattern>(box, ExpandCollapsePattern.Pattern);
        if (expand is null) return false;
        try
        {
            expand.Expand();
            Thread.Sleep(200);
            return TrySelect(box, value) ?? false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    expand.Collapse();
                    Thread.Sleep(150);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException)
            {
            }
        }
    }

    /// <summary>Null: keine Option mit dem Namen da. False: da, lässt sich aber nicht auswählen.</summary>
    static bool? TrySelect(AutomationElement box, string value)
    {
        var option = box.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, value, PropertyConditionFlags.IgnoreCase));
        if (option is null) return null;
        if (Pattern<SelectionItemPattern>(option, SelectionItemPattern.Pattern) is not { } item) return false;
        try
        {
            // Firefox nimmt Select() ohne Fehler an und tut nichts: nachsehen, ob die Option wirklich gewählt ist
            item.Select();
            var deadline = Environment.TickCount64 + 400;
            while (!item.Current.IsSelected && Environment.TickCount64 < deadline)
                Thread.Sleep(30);
            return item.Current.IsSelected;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException)
        {
            return false;
        }
    }

    static bool? OnOff(string value) => value.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "1" or "yes" or "ja" or "x" => true,
        "off" or "false" or "0" or "no" or "nein" or "" => false,
        _ => null,
    };

    static T? Pattern<T>(AutomationElement element, AutomationPattern pattern) where T : BasePattern =>
        element.TryGetCurrentPattern(pattern, out var found) ? (T)found : null;

    /// <summary>Linksklick an einer Bildschirmposition, danach steht die Maus wieder, wo sie war.</summary>
    static void MouseClick(Point point)
    {
        GetCursorPos(out var before);
        SetCursorPos((int)point.X, (int)point.Y);
        INPUT[] inputs =
        [
            new() { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
            new() { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        Thread.Sleep(50);
        SetCursorPos(before.X, before.Y);
    }

    // --- Picker ---

    static readonly HashSet<ControlType> Wrappers = [ControlType.Text, ControlType.Image, ControlType.Group, ControlType.Custom, ControlType.Pane];

    static readonly HashSet<ControlType> Interactive =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.RadioButton, ControlType.ComboBox, ControlType.Edit,
        ControlType.Hyperlink, ControlType.ListItem, ControlType.MenuItem, ControlType.TabItem, ControlType.TreeItem,
        ControlType.DataItem, ControlType.SplitButton, ControlType.Slider, ControlType.Spinner,
    ];

    /// <summary>
    /// Das Element an einer Bildschirmposition (physische Pixel), ohne die Fenster von QuickHasteBot selbst.
    /// Text in einem Button oder Link zählt als der Button bzw. Link.
    /// </summary>
    public static ElementInfo? Probe(int x, int y)
    {
        try
        {
            // Programme mit Administratorrechten zeigen uns nur ihr Fenster, nicht die Felder: gar nicht erst fragen
            var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);
            if (ProcessAccess.IsBlocked(root))
                return new ElementInfo(null, "", "", "", "", null, ProcessAccess.ProcessName(root), Rect.Empty, root, Blocked: true);

            var point = new Point(x, y);
            var element = AutomationElement.FromPoint(point);
            if (element is null || element.Current.ProcessId == Environment.ProcessId) return null;

            var walker = TreeWalker.ControlViewWalker;
            // Firefox meldet in einem Textfeld genau an der Schreibmarke das ganze Fenster: dann bis zum innersten
            // Element absteigen, dessen Rechteck den Punkt enthält
            if (Containers.Contains(element.Current.ControlType))
                element = DrillDown(walker, element, point);
            for (var i = 0; i < 3 && Wrappers.Contains(element.Current.ControlType); i++)
            {
                var parent = walker.GetParent(element);
                if (parent is null || !Interactive.Contains(parent.Current.ControlType)) break;
                element = parent;
            }

            var current = element.Current;
            return new ElementInfo(element, current.AutomationId ?? "", current.Name ?? "", current.ClassName ?? "",
                current.ControlType.ProgrammaticName.Replace("ControlType.", ""), ValueOf(element), ProcessName(current.ProcessId),
                current.BoundingRectangle, root);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    static readonly HashSet<ControlType> Containers = [ControlType.Window, ControlType.Pane, ControlType.Document];

    static AutomationElement DrillDown(TreeWalker walker, AutomationElement element, Point point)
    {
        for (var depth = 0; depth < 40; depth++)
        {
            AutomationElement? hit = null;
            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            {
                // Das letzte passende Kind liegt oben (später gezeichnet), z.B. ein Popup über der Seite
                if (child.Current.BoundingRectangle.Contains(point))
                    hit = child;
            }
            if (hit is null) break;
            element = hit;
        }
        return element;
    }

    static string? ValueOf(AutomationElement element)
    {
        if (Pattern<TogglePattern>(element, TogglePattern.Pattern) is { } toggle)
            return toggle.Current.ToggleState == ToggleState.On ? "on" : "off";
        if (Pattern<ValuePattern>(element, ValuePattern.Pattern) is { } value)
            return value.Current.Value is { Length: > 200 } text ? text[..200] : value.Current.Value;
        return null;
    }

    static string ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>
    /// Die Selektoren aus <see cref="ElementSelector.Candidates"/>, die im Fenster des Elements wirklich genau dieses
    /// Element finden. Findet keiner es, kommt type=…;index=n dazu.
    /// </summary>
    public static IReadOnlyList<ElementSelector> VerifiedSelectors(ElementInfo info)
    {
        if (info.Element is null) return [];
        var window = TopLevel(info.Element);
        var type = ElementSelector.Types.ContainsKey(ElementSelector.TypeAlias(info.Type)) ? info.Type : null;
        var verified = new List<ElementSelector>();
        foreach (var candidate in ElementSelector.Candidates(info.Id, info.Name, info.ClassName, type))
        {
            if (Hits(window, candidate, info.Element))
                verified.Add(candidate);
        }
        if (verified.Count > 0 || type is null) return verified;

        // Letzter Ausweg: das wievielte Element dieses Typs (mit dieser Beschriftung) im Fenster
        var basis = new ElementSelector(null, string.IsNullOrEmpty(info.Name) ? null : info.Name, null, [], type, 1);
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlTypes[type]),
            basis.Name is null ? Condition.TrueCondition : new PropertyCondition(AutomationElement.NameProperty, basis.Name, PropertyConditionFlags.IgnoreCase));
        var index = 0;
        foreach (AutomationElement candidate in window.FindAll(TreeScope.Descendants, condition))
        {
            index++;
            if (Automation.Compare(candidate, info.Element))
            {
                verified.Add(basis with { Index = index });
                break;
            }
        }
        return verified;
    }

    static bool Hits(AutomationElement window, ElementSelector selector, AutomationElement element)
    {
        try
        {
            return Find(window, selector) is { } found && Automation.Compare(found, element);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Das Hauptfenster, in dem {focus,…} später suchen wird (das Vordergrundfenster).</summary>
    static AutomationElement TopLevel(AutomationElement element)
    {
        var walker = TreeWalker.RawViewWalker;
        var root = AutomationElement.RootElement;
        var current = element;
        while (walker.GetParent(current) is { } parent && !Automation.Compare(parent, root))
            current = parent;
        return current;
    }
}
