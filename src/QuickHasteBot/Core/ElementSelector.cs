using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickHasteBot.Core;

/// <summary>
/// Beschreibt ein Element in einem Fenster für {focus,…}, {click,…} und {set,…}, gefunden per UI Automation.
/// In Browsern ist <see cref="Id"/> das HTML-id, <see cref="ClassName"/> das class-Attribut und <see cref="Name"/>
/// die Beschriftung (label, aria-label, Text eines Buttons). In Windows-Programmen sind es AutomationId,
/// Fensterklasse und Beschriftung des Steuerelements.
/// </summary>
/// <param name="Classes">Einzelne Klassen aus ".btn.primary", das Element muss sie alle haben.</param>
/// <param name="Index">Das wievielte passende Element, ab 1.</param>
/// <param name="Window">window=: dieses Programmfenster zuerst nach vorne holen und darin suchen, sonst im vorderen Fenster.</param>
/// <param name="HelpText">help=: der Tooltip, im Browser das title-Attribut oder der Platzhalter eines Felds.</param>
/// <param name="Within">"Container > Element": nur in Elementen suchen, die dazu passen. Hat selbst nie ein Window.</param>
public sealed partial record ElementSelector(
    string? Id, string? Name, string? ClassName, IReadOnlyList<string> Classes, string? Type, int Index, WindowSelector? Window = null,
    string? HelpText = null, ElementSelector? Within = null)
{
    /// <summary>Trennt Container und Element: "name=Lieferadresse > name=Strasse". Mit Leerzeichen, damit "Weiter >" ein Name bleibt.</summary>
    public const string ContainerSeparator = " > ";

    /// <summary>Die Typen, die man bei type= angeben kann, mit den Namen von UI Automation (ControlType.*).</summary>
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["edit"] = "Edit", ["input"] = "Edit", ["textbox"] = "Edit", ["field"] = "Edit",
        ["button"] = "Button", ["checkbox"] = "CheckBox", ["radio"] = "RadioButton", ["radiobutton"] = "RadioButton",
        ["combobox"] = "ComboBox", ["dropdown"] = "ComboBox", ["select"] = "ComboBox",
        ["link"] = "Hyperlink", ["hyperlink"] = "Hyperlink", ["list"] = "List", ["listitem"] = "ListItem", ["option"] = "ListItem",
        ["menu"] = "Menu", ["menuitem"] = "MenuItem", ["tab"] = "TabItem", ["tabitem"] = "TabItem",
        ["tree"] = "Tree", ["treeitem"] = "TreeItem", ["text"] = "Text", ["document"] = "Document",
        ["slider"] = "Slider", ["spinner"] = "Spinner", ["image"] = "Image", ["group"] = "Group", ["pane"] = "Pane",
        ["table"] = "Table", ["dataitem"] = "DataItem", ["window"] = "Window",
    };

    /// <summary>
    /// "#email", ".btn.primary", "id=email", "name=E-Mail;type=edit", "class=btn primary;index=2", "help=Drucken" oder
    /// einfach "Absenden" (= name). Teile werden mit ; kombiniert, Container mit " > " davorgestellt:
    /// "name=Lieferadresse;type=group > name=Strasse".
    /// </summary>
    public static bool TryParse(string text, out ElementSelector selector, out string error)
    {
        selector = new ElementSelector(null, null, null, [], null, 1);
        ElementSelector? chain = null;
        WindowSelector? window = null;
        foreach (var segment in text.Split(ContainerSeparator))
        {
            if (!TryParseOne(segment, out var part, out error))
                return false;
            if (part.Window is not null)
            {
                if (window is not null)
                {
                    error = "window= may only appear once in an element selector";
                    return false;
                }
                window = part.Window;
            }
            chain = part with { Window = null, Within = chain };
        }
        selector = chain! with { Window = window };
        error = "";
        return true;
    }

    /// <summary>Ein Selektor ohne " > ".</summary>
    static bool TryParseOne(string text, out ElementSelector selector, out string error)
    {
        selector = new ElementSelector(null, null, null, [], null, 1);
        error = "";
        string? id = null, name = null, className = null, type = null, help = null;
        WindowSelector? window = null;
        var classes = new List<string>();
        var index = 1;

        foreach (var raw in text.Split(';'))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;

            if (part.StartsWith('#'))
            {
                id = part[1..];
                continue;
            }
            if (part.StartsWith('.'))
            {
                classes.AddRange(part.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }

            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                name = part; // "Absenden" = Beschriftung
                continue;
            }

            var key = part[..eq].Trim().ToLowerInvariant();
            var value = part[(eq + 1)..].Trim();
            switch (key)
            {
                case "id": id = value; break;
                case "name": name = value; break;
                case "class": className = value; break;
                case "help" or "tooltip": help = value; break;
                case "type":
                    if (!Types.TryGetValue(value, out var uia))
                    {
                        error = $"unknown element type \"{value}\", e.g. edit, button, checkbox, combobox, link";
                        return false;
                    }
                    type = uia;
                    break;
                case "window":
                    if (!WindowSelector.TryParse(value, out var w, out error))
                        return false;
                    window = w;
                    break;
                case "index":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index) || index < 1)
                    {
                        error = $"index needs a number from 1, not \"{value}\"";
                        return false;
                    }
                    break;
                default:
                    error = $"unknown selector part \"{key}=\", use id, name, class, type, help, index or window";
                    return false;
            }
        }

        if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(className) && classes.Count == 0
            && type is null && string.IsNullOrEmpty(help))
        {
            error = $"empty element selector \"{text.Trim()}\", e.g. #email or name=E-Mail";
            return false;
        }

        selector = new ElementSelector(NullIfEmpty(id), NullIfEmpty(name), NullIfEmpty(className), classes, type, index, window,
            NullIfEmpty(help));
        return true;
    }

    static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// name= mit *: "Posteingang*" findet auch "Posteingang - 2 ungelesene Elemente". UIA kann das nicht selbst
    /// suchen, die Treffer werden danach gefiltert.
    /// </summary>
    public bool NameIsPattern => Name is not null && Wildcard.IsPattern(Name);

    /// <summary>Ob eine Beschriftung zu name= passt: gleich (ohne Gross-/Kleinschreibung) oder nach dem Muster mit *.</summary>
    public bool NameMatches(string? name) => Name is null || Wildcard.IsMatch(Name, name ?? "");

    public bool HelpIsPattern => HelpText is not null && Wildcard.IsPattern(HelpText);

    /// <summary>Wie <see cref="NameMatches"/>, für help=.</summary>
    public bool HelpMatches(string? help) => HelpText is null || Wildcard.IsMatch(HelpText, help ?? "");

    /// <summary>
    /// Kurzform wie man sie schreiben würde: "#email", "name=Absenden;type=button", ".btn.primary;index=2",
    /// "name=Lieferadresse > name=Strasse;window=crm".
    /// </summary>
    public override string ToString()
    {
        var text = Chain();
        return Window is null ? text : text + ";window=" + Window;
    }

    string Chain()
    {
        var parts = new List<string>();
        if (Id is not null) parts.Add("#" + Id);
        if (Classes.Count > 0) parts.Add("." + string.Join('.', Classes));
        if (Name is not null) parts.Add("name=" + Name);
        if (ClassName is not null) parts.Add("class=" + ClassName);
        if (HelpText is not null) parts.Add("help=" + HelpText);
        if (Type is not null) parts.Add("type=" + TypeAlias(Type));
        if (Index > 1) parts.Add("index=" + Index.ToString(CultureInfo.InvariantCulture));
        var own = string.Join(';', parts);
        return Within is null ? own : Within.Chain() + ContainerSeparator + own;
    }

    /// <summary>Ob sich ein Text als Wert in einem Selektor schreiben lässt (ohne ; und " > ", die Teile trennen).</summary>
    public static bool Writable(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains(';') && !value.Contains(ContainerSeparator);

    /// <summary>Kurzer Name für type=, z.B. "Edit" → "edit", "Hyperlink" → "link".</summary>
    public static string TypeAlias(string uiaType) => uiaType switch
    {
        "Hyperlink" => "link",
        "RadioButton" => "radio",
        "TabItem" => "tab",
        _ => uiaType.ToLowerInvariant(),
    };

    // React (:r3:), GUIDs, lange Zahlen mitten in Buchstaben (mui-12345, input_1699457) und zufällige Buchstaben-Ziffern-
    // Folgen (k3x9q2mz: mindestens 7 Zeichen, 2 Ziffern, Ziffer und Buchstabe gemischt) wechseln bei jedem Laden.
    // Dazu Präfixe von Bibliotheken, die ihre ids immer selbst erzeugen (mantine-b1omqnnct, radix-:r2:, ember123).
    [GeneratedRegex(@"^(?:mantine|radix|headlessui|react-aria\d*|downshift|chakra)-|^(?:ember|ui-id-|j_idt)\d|^:[a-z0-9]+:$|[0-9a-f]{8}-[0-9a-f]{4}-|[a-z][-_]?\d{4,}|\d{4,}[-_]?[a-z]|(?:^|[-_])(?=[a-z0-9]{7,}(?:$|[-_]))(?=(?:[a-z]*\d){2})(?=[a-z0-9]*\d[a-z])(?=[a-z0-9]*[a-z]\d)", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedId();

    /// <summary>
    /// Ids, die eine Web-App bei jedem Laden neu erzeugt. Reine Zahlen gelten als stabil: in Windows-Programmen sind
    /// das die festen Control-IDs.
    /// </summary>
    public static bool LooksGenerated(string id) => GeneratedId().IsMatch(id);

    /// <summary>
    /// Selektoren für ein Element, das der Picker erfasst hat, der stabilste zuerst. Der Picker prüft danach,
    /// welcher davon wirklich genau dieses Element findet.
    /// </summary>
    public static IReadOnlyList<ElementSelector> Candidates(string? id, string? name, string? className, string? type, string? help = null)
    {
        var list = new List<ElementSelector>();
        var classes = (className ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasId = Writable(id);
        var hasName = Writable(name);
        var hasHelp = Writable(help) && help != name;

        if (hasId && !LooksGenerated(id!)) Add(id, null, null, []);
        if (hasName) Add(null, name, null, [], type);
        if (classes.Length > 0) Add(null, null, null, classes, type);
        if (hasName) Add(null, name, null, []);
        // Tooltip: z.B. Buttons, die nur ein Symbol zeigen
        if (hasHelp) Add(null, null, null, [], type, help);
        if (hasId && LooksGenerated(id!)) Add(id, null, null, []);
        return list;

        void Add(string? i, string? n, string? c, IReadOnlyList<string> cs, string? t = null, string? h = null)
        {
            var candidate = new ElementSelector(i, n, c, cs, t, 1, HelpText: h);
            if (!list.Any(x => x.ToString() == candidate.ToString()))
                list.Add(candidate);
        }
    }
}
