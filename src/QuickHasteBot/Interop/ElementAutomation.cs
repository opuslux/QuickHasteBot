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
/// <param name="HelpText">Der Tooltip, im Browser title-Attribut oder Platzhalter.</param>
internal sealed record ElementInfo(
    AutomationElement? Element, string Id, string Name, string ClassName, string Type, string? Value, string Process, Rect Bounds,
    nint Window, bool Blocked = false, string HelpText = "");

/// <summary>
/// Elemente per UI Automation finden und bedienen: {focus,…}, {click,…}, {set,…}, {dblclick,…}, {rclick,…} und der
/// Element-Picker.
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

    /// <summary>
    /// Sucht das Element im Fenster und führt die Aktion aus. Wartet bis <see cref="FindTimeout"/>, bis es da und
    /// freigegeben ist: Viele Formulare geben ein Feld erst frei, wenn das davor ausgefüllt ist (die Uhrzeit erst nach
    /// dem Datum). Bleibt es gesperrt, wird es trotzdem versucht, und die Fehlermeldung sagt warum.
    /// </summary>
    public static ElementResult Run(ElementStep step, nint window, Func<bool> cancelled)
    {
        var deadline = Environment.TickCount64 + (long)FindTimeout.TotalMilliseconds;
        AutomationElement? element = null;
        while (true)
        {
            var enabled = false;
            try
            {
                // Jedes Mal neu suchen: Webseiten ersetzen ein Feld oft, wenn sie es freigeben
                element = FindInWindow(window, step.Selector) ?? element;
                enabled = element?.Current.IsEnabled == true;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                // Fenster oder Baum im Umbau (Seite lädt), gleich nochmals
            }
            if (enabled) break;
            if (cancelled()) return new(ElementOutcome.Cancelled);
            if (Environment.TickCount64 > deadline)
            {
                if (element is null) return new(ElementOutcome.NotFound);
                break;
            }
            Thread.Sleep(PollInterval);
        }

        try
        {
            return step.Action switch
            {
                ElementAction.Focus => Focus(element),
                ElementAction.Click => Click(element),
                ElementAction.DoubleClick => MouseOnly(element, MouseButton.Left, 2),
                ElementAction.RightClick => MouseOnly(element, MouseButton.Right, 1),
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
            return FindInWindow(window, selector)?.Current.BoundingRectangle is { IsEmpty: false } bounds ? bounds : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sucht zuerst in den offenen Menüs und Popups des Fensters (<see cref="WindowFinder.Popups"/>), dann im Fenster
    /// selbst. So findet {click,name=Kopieren} nach einem {rclick,…} den Eintrag im Kontextmenü, auch wenn das Fenster
    /// einen Button gleichen Namens hat.
    /// </summary>
    static AutomationElement? FindInWindow(nint window, ElementSelector selector)
    {
        foreach (var popup in WindowFinder.Popups(window))
        {
            try
            {
                if (Find(AutomationElement.FromHandle(popup), selector) is { } found)
                    return found;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                // Das Menü ist inzwischen zu
            }
        }
        return Find(AutomationElement.FromHandle(window), selector);
    }

    /// <summary>
    /// Das erste passende Element unter <paramref name="root"/>, oder das n-te bei index=. Mit Container
    /// ("Container > Element") nur in den passenden Containern, der Reihe nach; index= zählt über alle hinweg.
    /// </summary>
    public static AutomationElement? Find(AutomationElement root, ElementSelector selector)
    {
        if (selector.Within is null)
            return FindIn(root, selector);

        var seen = new HashSet<string>();
        var position = 0;
        foreach (var scope in Scopes(root, selector.Within))
        {
            if (selector.Index == 1)
            {
                if (FindIn(scope, selector) is { } first) return first;
                continue;
            }
            // Verschachtelte Container (Gruppe in Gruppe) liefern dieselben Treffer mehrmals: nur einmal zählen
            foreach (var match in Targets(scope, selector))
                if (seen.Add(Key(match)) && ++position == selector.Index)
                    return match;
        }
        return null;
    }

    /// <summary>
    /// Die Container, in denen gesucht wird: alle passenden der Reihe nach, mit index= nur der n-te. So findet
    /// "name=Posteingang*" im zweiten Postfach, wenn das erste keinen hat.
    /// </summary>
    static IEnumerable<AutomationElement> Scopes(AutomationElement root, ElementSelector container)
    {
        IEnumerable<AutomationElement> parents = container.Within is null ? [root] : Scopes(root, container.Within);
        var seen = new HashSet<string>();
        var position = 0;
        foreach (var parent in parents)
        {
            foreach (var match in Matches(parent, container))
            {
                // Ohne Kinder kann nichts darin liegen: z.B. die Überschrift einer Gruppe, die gleich heisst wie die
                // Gruppe. Zählte sie mit, träfe index= die Falsche.
                if (!seen.Add(Key(match)) || TreeWalker.ControlViewWalker.GetFirstChild(match) is null) continue;
                position++;
                if (container.Index == 1) yield return match;
                else if (position == container.Index)
                {
                    yield return match;
                    yield break;
                }
            }
        }
    }

    /// <summary>Ohne Container.</summary>
    static AutomationElement? FindIn(AutomationElement root, ElementSelector selector)
    {
        if (selector.Classes.Count == 0 && selector.Index == 1 && !selector.NameIsPattern && !selector.HelpIsPattern)
        {
            var first = root.FindFirst(TreeScope.Descendants, ConditionFor(selector));
            // Zuerst ein Text gefunden: vielleicht die Beschriftung, und das Feld dazu kommt erst danach
            if (first is null || selector.Type is not null || first.Current.ControlType != ControlType.Text)
                return first;
        }
        return Targets(root, selector).Skip(selector.Index - 1).FirstOrDefault();
    }

    /// <summary>
    /// Die Treffer, die gemeint sind. Ohne type= ist ein Text, der gleich heisst wie ein anderer Treffer, dessen
    /// Beschriftung: Chrome zeigt &lt;label&gt;Nachname&lt;/label&gt; als eigenes Text-Element vor dem Feld, das ebenfalls
    /// "Nachname" heisst. Dann zählen nur die übrigen, auch für index=. Passen nur Texte, bleiben sie Treffer.
    /// </summary>
    static List<AutomationElement> Targets(AutomationElement root, ElementSelector selector)
    {
        var all = Matches(root, selector).ToList();
        if (selector.Type is not null) return all;
        var fields = all.Where(e => e.Cached.ControlType != ControlType.Text).ToList();
        return fields.Count > 0 ? fields : all;
    }

    /// <summary>
    /// Alle passenden Elemente unter <paramref name="root"/>, ohne index= und Container. Einzelne Klassen und Muster mit *
    /// kann UIA nicht selbst: alle Treffer der übrigen Angaben holen, Klassen, Name und Tooltip gleich mit (ein Aufruf
    /// statt einer pro Element), und danach filtern.
    /// </summary>
    static IEnumerable<AutomationElement> Matches(AutomationElement root, ElementSelector selector)
    {
        var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.Full };
        cache.Add(AutomationElement.ClassNameProperty);
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.HelpTextProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.RuntimeIdProperty);
        AutomationElementCollection all;
        using (cache.Activate())
            all = root.FindAll(TreeScope.Descendants, ConditionFor(selector));

        foreach (AutomationElement candidate in all)
        {
            if (selector.Classes.Count > 0)
            {
                var classes = candidate.Cached.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!selector.Classes.All(c => classes.Contains(c, StringComparer.Ordinal))) continue;
            }
            if (selector.NameIsPattern && !selector.NameMatches(candidate.Cached.Name)) continue;
            if (selector.HelpIsPattern && !selector.HelpMatches(candidate.Cached.HelpText)) continue;
            yield return candidate;
        }
    }

    /// <summary>Was UIA selbst prüfen kann: id, genauer Name und Tooltip, Fensterklasse, Typ.</summary>
    static Condition ConditionFor(ElementSelector selector)
    {
        var conditions = new List<Condition>();
        if (selector.Id is not null)
            conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, selector.Id));
        if (selector.Name is not null && !selector.NameIsPattern)
            conditions.Add(new PropertyCondition(AutomationElement.NameProperty, selector.Name, PropertyConditionFlags.IgnoreCase));
        if (selector.HelpText is not null && !selector.HelpIsPattern)
            conditions.Add(new PropertyCondition(AutomationElement.HelpTextProperty, selector.HelpText, PropertyConditionFlags.IgnoreCase));
        if (selector.ClassName is not null)
            conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, selector.ClassName));
        if (selector.Type is not null)
            conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlTypes[selector.Type]));
        return conditions.Count switch
        {
            0 => Condition.TrueCondition,
            1 => conditions[0],
            _ => new AndCondition([.. conditions]),
        };
    }

    /// <summary>Kennung eines Elements, solange es lebt (aus dem Cache von <see cref="Matches"/>).</summary>
    static string Key(AutomationElement element) =>
        string.Join('.', (int[])element.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty));

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
        else if (!element.TryGetClickablePoint(out var point))
            return new(ElementOutcome.Failed, Error: "the element can't be clicked");
        // z.B. ein <div> mit onclick: nur ein echter Klick hilft
        else if (!MouseClick(point))
            return new(ElementOutcome.Failed, Error: "the element is outside the screen and can't be clicked");
        return new(ElementOutcome.Done);
    }

    /// <summary>
    /// Doppel- und Rechtsklick: Dafür hat UI Automation kein Muster, es braucht immer einen echten Klick. Liegt das
    /// Element in einer Liste ausserhalb des sichtbaren Bereichs, wird es zuerst hineingescrollt.
    /// </summary>
    static ElementResult MouseOnly(AutomationElement element, MouseButton button, int clicks)
    {
        if (element.Current.IsOffscreen)
            Pattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern)?.ScrollIntoView();
        if (!element.TryGetClickablePoint(out var point))
            return new(ElementOutcome.Failed, Error: "the element can't be clicked");
        if (!MouseClick(point, button, clicks))
            return new(ElementOutcome.Failed, Error: "the element is outside the screen and can't be clicked");
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

        // Schreibgeschütztes Feld, das eine Liste aufklappt (Auswahlfelder aus Web-Bibliotheken): Eintippen ginge ins
        // Leere, darum lieber melden, dass die Option fehlt
        if (Pattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern) is not null)
            return SelectOption(element, value)
                ? new(ElementOutcome.Done)
                : new(ElementOutcome.Failed, Error: $"no option \"{value}\" in the list");

        element.SetFocus();
        return new(ElementOutcome.TypeInstead, value);
    }

    /// <summary>
    /// Option mit diesem Namen auswählen. Erst ohne Aufklappen (Browser haben die Optionen auch zugeklappt im Baum),
    /// nur wenn sie fehlt aufklappen. Firefox lässt nicht direkt auswählen: dann false, und es wird eingetippt,
    /// was die Option im fokussierten Dropdown wählt. Ein offenes Dropdown würde das Eintippen schlucken.
    /// Web-Bibliotheken hängen die Liste oft ans Ende der Seite statt unter das Feld: dann dort suchen (<see cref="PickFromPopup"/>).
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
            return TrySelect(box, value) ?? PickFromPopup(box, value);
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

    /// <summary>
    /// Die aufgeklappte Liste steht nicht unter dem Feld: die Option im ganzen Dokument suchen. Klappt das Aufklappen
    /// per UIA nicht und lässt sich nichts eintippen, mit einem echten Klick aufs Feld öffnen. Auswählen per UIA, sonst
    /// mit einem echten Klick auf die Option. True nur, wenn die Wahl nachweislich angekommen ist.
    /// </summary>
    static bool PickFromPopup(AutomationElement box, string value)
    {
        var option = PopupOption(box, value, 400);
        if (option is null && Pattern<ValuePattern>(box, ValuePattern.Pattern) is null or { Current.IsReadOnly: true }
            && box.TryGetClickablePoint(out var point) && MouseClick(point))
            option = PopupOption(box, value, 800);
        if (option is null) return false;

        if (Pattern<SelectionItemPattern>(option, SelectionItemPattern.Pattern) is { } item)
        {
            try
            {
                item.Select();
            }
            catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException or COMException)
            {
            }
            if (Chosen(box, option, value)) return true;
        }
        if (!option.TryGetClickablePoint(out var optionPoint))
        {
            Pattern<ScrollItemPattern>(option, ScrollItemPattern.Pattern)?.ScrollIntoView();
            if (!option.TryGetClickablePoint(out optionPoint)) return false;
        }
        return MouseClick(optionPoint) && Chosen(box, option, value);
    }

    static readonly Condition OptionTypes = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));

    /// <summary>Eine Listenoption mit diesem Namen im Dokument des Felds, sichtbare zuerst. Wartet bis <paramref name="wait"/> ms.</summary>
    static AutomationElement? PopupOption(AutomationElement box, string value, int wait)
    {
        var root = DocumentOf(box);
        var condition = new AndCondition(OptionTypes, new PropertyCondition(AutomationElement.NameProperty, value, PropertyConditionFlags.IgnoreCase));
        var deadline = Environment.TickCount64 + wait;
        while (true)
        {
            var found = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>().OrderBy(e => e.Current.IsOffscreen).FirstOrDefault();
            if (found is not null || Environment.TickCount64 > deadline) return found;
            Thread.Sleep(50);
        }
    }

    /// <summary>Die Webseite (bzw. der iframe), in der das Feld steht, sonst sein Fenster.</summary>
    static AutomationElement DocumentOf(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;
        while (walker.GetParent(current) is { } parent && parent != AutomationElement.RootElement)
        {
            current = parent;
            if (current.Current.ControlType == ControlType.Document) break;
        }
        return current;
    }

    /// <summary>Ob die Option gewählt ist: als ausgewählt gemeldet, oder das Feld zeigt jetzt ihren Namen. Wartet kurz.</summary>
    static bool Chosen(AutomationElement box, AutomationElement option, string value)
    {
        var deadline = Environment.TickCount64 + 600;
        while (true)
        {
            try
            {
                if (Pattern<SelectionItemPattern>(option, SelectionItemPattern.Pattern) is { Current.IsSelected: true }) return true;
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
            {
                // Liste schon zu, die Option gibt es nicht mehr: das Feld entscheidet
            }
            var shown = Pattern<ValuePattern>(box, ValuePattern.Pattern)?.Current.Value ?? box.Current.Name;
            if (string.Equals(shown?.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            if (Environment.TickCount64 > deadline) return false;
            Thread.Sleep(50);
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

    enum MouseButton { Left, Right }

    /// <summary>
    /// Ein Klick (oder ein Doppelklick bei <paramref name="clicks"/> = 2) an einer Bildschirmposition, danach steht die
    /// Maus wieder, wo sie war. False ohne Klick, wenn der Punkt auf keinem Bildschirm liegt: UIA liefert auch für
    /// Elemente in Fenstern ausserhalb einen Punkt, Windows setzt den Zeiger dann an den Rand, und der Klick träfe ein
    /// fremdes Fenster.
    /// </summary>
    static bool MouseClick(Point point, MouseButton button = MouseButton.Left, int clicks = 1)
    {
        if (MonitorFromPoint(new POINT { X = (int)point.X, Y = (int)point.Y }, MONITOR_DEFAULTTONULL) == 0)
            return false;
        // SendInput meint die Tasten, wie sie an der Maus sitzen: Bei vertauschten Tasten (Linkshänder) ist links rechts
        var physical = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? (button == MouseButton.Left ? MouseButton.Right : MouseButton.Left) : button;
        var (down, up) = physical == MouseButton.Left ? (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP) : (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
        GetCursorPos(out var before);
        SetCursorPos((int)point.X, (int)point.Y);
        // Beim Doppelklick alle vier auf einmal: Windows wertet sie als Doppelklick, weil sie innerhalb der Doppelklickzeit
        // an derselben Stelle ankommen
        var inputs = Enumerable.Range(0, clicks * 2)
            .Select(i => new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = i % 2 == 0 ? down : up } } })
            .ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        Thread.Sleep(50);
        SetCursorPos(before.X, before.Y);
        return true;
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
            // Tag, Monat, Jahr eines Datumsfelds zählen als das Feld. Den Button darin (Kalender, Passwort anzeigen)
            // meldet Chrome nur genau in seiner Mitte, daneben das ganze Feld: dann selbst nachschauen.
            if (DateFieldOf(element) is { } date)
                element = date;
            if (element.Current.ControlType == ControlType.Edit || IsDateField(element))
                element = ButtonAt(element, point) ?? element;

            return Describe(element, root);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>In Chrome und Edge der Teil eines Datums- oder Zeitfelds mit den Teilfeldern (Tag, Monat, Jahr …).</summary>
    const string DateTimeEdit = "date-time-edit";

    /// <summary>
    /// Das Datums- oder Zeitfeld (Chrome, Edge), zu dem ein Teilfeld wie "Tag" gehört, oder das Feld selbst. Sonst null.
    /// Die Teilfelder sind Spinner, das Feld ein Edit (Datum) oder eine Group (Uhrzeit).
    /// </summary>
    public static AutomationElement? DateFieldOf(AutomationElement element)
    {
        if (IsDateField(element)) return element;
        var walker = TreeWalker.RawViewWalker;
        var current = element;
        for (var i = 0; i < 3 && walker.GetParent(current) is { } parent; i++)
        {
            if (parent.Current.AutomationId == DateTimeEdit) return walker.GetParent(parent);
            current = parent;
        }
        return null;
    }

    /// <summary>Für den Recorder: wie <see cref="DateFieldOf"/>, auch für den Kalender-Button im Feld.</summary>
    public static AutomationElement? DateFieldOfPart(AutomationElement element) =>
        DateFieldOf(element) ?? (element.Current.ControlType == ControlType.Button
            && TreeWalker.RawViewWalker.GetParent(element) is { } parent && IsDateField(parent) ? parent : null);

    /// <summary>
    /// Das Dropdown, zu dem eine Option gehört. Chrome meldet beim Aufklappen den Fokus zuerst auf der gewählten Option,
    /// nicht auf dem Dropdown. Null, wenn die Option nicht unter einem Dropdown hängt.
    /// </summary>
    public static AutomationElement? ComboBoxOf(AutomationElement option)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = option;
        for (var i = 0; i < 3 && walker.GetParent(current) is { } parent; i++)
        {
            if (parent.Current.ControlType == ControlType.ComboBox) return parent;
            current = parent;
        }
        return null;
    }

    /// <summary>Die Teilfelder hängen im Raw-Baum als erstes Kind unter dem Feld (im Control-Baum fehlt die Gruppe).</summary>
    static bool IsDateField(AutomationElement element)
    {
        var type = element.Current.ControlType;
        return (type == ControlType.Edit || type == ControlType.Group)
            && TreeWalker.RawViewWalker.GetFirstChild(element) is { } first && first.Current.AutomationId == DateTimeEdit;
    }

    static AutomationElement? ButtonAt(AutomationElement field, Point point)
    {
        foreach (AutomationElement button in field.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            if (button.Current.BoundingRectangle.Contains(point))
                return button;
        }
        return null;
    }

    /// <summary>
    /// Der Wert eines Datums- oder Zeitfelds so, wie man ihn eintippt (<see cref="Recording.TypedDate"/>), z.B.
    /// "20112026". Leer, wenn das Feld (noch) kein vollständiges Datum hat. Null, wenn es weg ist.
    /// </summary>
    public static string? DateDigits(AutomationElement field)
    {
        try
        {
            if (Pattern<ValuePattern>(field, ValuePattern.Pattern) is not { } value) return null;
            if (value.Current.Value.Length == 0) return "";
            var walker = TreeWalker.RawViewWalker;
            if (walker.GetFirstChild(field) is not { } edit || edit.Current.AutomationId != DateTimeEdit) return null;
            var parts = new List<string>();
            Collect(edit, 0);
            return Recording.TypedDate(parts);

            void Collect(AutomationElement parent, int depth)
            {
                for (var child = walker.GetFirstChild(parent); child is not null; child = walker.GetNextSibling(child))
                {
                    if (child.Current.ControlType == ControlType.Spinner)
                        parts.Add(Pattern<ValuePattern>(child, ValuePattern.Pattern)?.Current.Value ?? "");
                    else if (depth < 3)
                        Collect(child, depth + 1);
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Was Picker und Recorder über ein Element wissen. <paramref name="root"/>: sein Hauptfenster.</summary>
    static ElementInfo Describe(AutomationElement element, nint root)
    {
        var current = element.Current;
        return new ElementInfo(element, current.AutomationId ?? "", current.Name ?? "", current.ClassName ?? "",
            TypeName(current.ControlType), ValueOf(element), ProcessName(current.ProcessId),
            current.BoundingRectangle, root, HelpText: current.HelpText ?? "");
    }

    /// <summary>Wie <see cref="Probe"/>, für ein Element, das man schon hat (z.B. das fokussierte). Null, wenn es weg ist.</summary>
    public static ElementInfo? Describe(AutomationElement element)
    {
        try
        {
            var top = TopLevel(element).Current.NativeWindowHandle;
            return Describe(element, top == 0 ? 0 : GetAncestor(top, GA_ROOT));
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Der ganze Wert eines Felds für den Recorder: "on"/"off" bei Checkboxen, der Text eines Textfelds, die gewählte
    /// Option eines Dropdowns. Null, wenn das Feld keinen verrät oder nicht mehr da ist.
    /// </summary>
    public static string? FullValue(AutomationElement element)
    {
        try
        {
            if (Pattern<TogglePattern>(element, TogglePattern.Pattern) is { } toggle)
                return toggle.Current.ToggleState == ToggleState.On ? "on" : "off";
            if (Pattern<ValuePattern>(element, ValuePattern.Pattern) is { } value)
                return value.Current.Value;
            // Windows-Dropdowns ohne Textfeld haben keinen Wert, nur eine Auswahl
            if (Pattern<SelectionPattern>(element, SelectionPattern.Pattern) is { } selection)
                return selection.Current.GetSelection() is [{ } selected, ..] ? selected.Current.Name : "";
            return null;
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
    public static IReadOnlyList<ElementSelector> VerifiedSelectors(ElementInfo info) => VerifiedSelectors(info, firstOnly: false);

    /// <summary>
    /// Für den Recorder: der stabilste Selektor, der genau dieses Element findet, sonst der stabilste ungeprüfte.
    /// Hört beim ersten Treffer auf, das spart in Browsern Sekunden. Null, wenn das Element nichts Brauchbares hat.
    /// </summary>
    public static ElementSelector? BestSelector(ElementInfo info)
    {
        try
        {
            if (VerifiedSelectors(info, firstOnly: true) is [var best, ..]) return best;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or ArgumentException)
        {
            // Element inzwischen weg (Seite gewechselt): dann eben ungeprüft
        }
        // Mit Typ: Ein Name allein passt oft zuerst auf die Beschriftung daneben ("Uhrzeit" über dem Dropdown)
        return ElementSelector.Candidates(info.Id, info.Name, info.ClassName, KnownType(info.Type), info.HelpText) is [var first, ..] ? first : null;
    }

    static List<ElementSelector> VerifiedSelectors(ElementInfo info, bool firstOnly)
    {
        if (info.Element is null) return [];
        var window = TopLevel(info.Element);
        var type = KnownType(info.Type);
        var candidates = ElementSelector.Candidates(info.Id, info.Name, info.ClassName, type, info.HelpText);
        var verified = new List<ElementSelector>();
        foreach (var candidate in candidates)
        {
            if (!Hits(window, candidate, info.Element)) continue;
            verified.Add(candidate);
            if (firstOnly) return verified;
        }
        if (verified.Count > 0) return verified;
        // Beim Aufnehmen nicht: Jeder Versuch ist eine Suche, in Browsern zu langsam, während man ein Formular ausfüllt
        if (!firstOnly && InContainer(info.Element, window, candidates) is { Count: > 0 } scoped) return scoped;
        if (type is null) return verified;

        // Letzter Ausweg: das wievielte Element dieses Typs (mit dieser Beschriftung) im Fenster. Ein * im Namen wäre
        // ein Muster und würde anders zählen.
        var basis = new ElementSelector(null, ElementSelector.Writable(info.Name) && !Wildcard.IsPattern(info.Name) ? info.Name : null,
            null, [], type, 1);
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

    /// <summary>
    /// Mehrere Elemente passen gleich gut, z.B. "Strasse" in Rechnungs- und Lieferadresse oder "Posteingang" in zwei
    /// Postfächern: im nächsten Vorfahren mit eigener id oder Beschriftung eingrenzen, "name=Lieferadresse > name=Strasse".
    /// Vorfahren ohne beides (in Browsern die meisten divs) werden übersprungen.
    /// </summary>
    static List<ElementSelector> InContainer(AutomationElement element, AutomationElement window, IReadOnlyList<ElementSelector> candidates)
    {
        var walker = TreeWalker.ControlViewWalker;
        var current = element;
        for (var level = 0; level < 8; level++)
        {
            var parent = walker.GetParent(current);
            if (parent is null || Automation.Compare(parent, window) || Automation.Compare(parent, AutomationElement.RootElement)) break;
            current = parent;

            var info = parent.Current;
            var id = ElementSelector.LooksGenerated(info.AutomationId ?? "") ? null : info.AutomationId;
            if (ElementSelector.Candidates(id, info.Name, null, KnownType(TypeName(info.ControlType))) is not [var container, ..])
                continue;
            var found = candidates.Select(c => c with { Within = container }).Where(c => Hits(window, c, element)).ToList();
            if (found.Count > 0) return found;
        }
        return [];
    }

    static string TypeName(ControlType type) => type.ProgrammaticName.Replace("ControlType.", "");

    /// <summary>Der UIA-Typ, wenn man ihn bei type= angeben kann, sonst null.</summary>
    static string? KnownType(string type) => ElementSelector.Types.ContainsKey(ElementSelector.TypeAlias(type)) ? type : null;

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
