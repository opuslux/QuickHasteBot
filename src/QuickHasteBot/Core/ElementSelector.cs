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
public sealed partial record ElementSelector(
    string? Id, string? Name, string? ClassName, IReadOnlyList<string> Classes, string? Type, int Index, WindowSelector? Window = null)
{
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
    /// "#email", ".btn.primary", "id=email", "name=E-Mail;type=edit", "class=btn primary;index=2" oder einfach
    /// "Absenden" (= name). Teile werden mit ; kombiniert.
    /// </summary>
    public static bool TryParse(string text, out ElementSelector selector, out string error)
    {
        selector = new ElementSelector(null, null, null, [], null, 1);
        error = "";
        string? id = null, name = null, className = null, type = null;
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
                    error = $"unknown selector part \"{key}=\", use id, name, class, type, index or window";
                    return false;
            }
        }

        if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(className) && classes.Count == 0 && type is null)
        {
            error = $"empty element selector \"{text}\", e.g. #email or name=E-Mail";
            return false;
        }

        selector = new ElementSelector(NullIfEmpty(id), NullIfEmpty(name), NullIfEmpty(className), classes, type, index, window);
        return true;
    }

    static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Kurzform wie man sie schreiben würde: "#email", "name=Absenden;type=button", ".btn.primary;index=2".</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Id is not null) parts.Add("#" + Id);
        if (Classes.Count > 0) parts.Add("." + string.Join('.', Classes));
        if (Name is not null) parts.Add("name=" + Name);
        if (ClassName is not null) parts.Add("class=" + ClassName);
        if (Type is not null) parts.Add("type=" + TypeAlias(Type));
        if (Index > 1) parts.Add("index=" + Index.ToString(CultureInfo.InvariantCulture));
        if (Window is not null) parts.Add("window=" + Window);
        return string.Join(';', parts);
    }

    /// <summary>Kurzer Name für type=, z.B. "Edit" → "edit", "Hyperlink" → "link".</summary>
    public static string TypeAlias(string uiaType) => uiaType switch
    {
        "Hyperlink" => "link",
        "RadioButton" => "radio",
        "TabItem" => "tab",
        _ => uiaType.ToLowerInvariant(),
    };

    // React (:r3:), GUIDs, lange Zahlen mitten in Buchstaben (mui-12345, input_1699457) und zufällige Buchstaben-Ziffern-
    // Folgen (mantine-2hi08ejzc: mindestens 7 Zeichen, 2 Ziffern, Ziffer und Buchstabe gemischt) wechseln bei jedem Laden
    [GeneratedRegex(@"^:[a-z0-9]+:$|[0-9a-f]{8}-[0-9a-f]{4}-|[a-z][-_]?\d{4,}|\d{4,}[-_]?[a-z]|(?:^|[-_])(?=[a-z0-9]{7,}(?:$|[-_]))(?=(?:[a-z]*\d){2})(?=[a-z0-9]*\d[a-z])(?=[a-z0-9]*[a-z]\d)", RegexOptions.IgnoreCase)]
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
    public static IReadOnlyList<ElementSelector> Candidates(string? id, string? name, string? className, string? type)
    {
        var list = new List<ElementSelector>();
        var classes = (className ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasId = !string.IsNullOrWhiteSpace(id);
        var hasName = !string.IsNullOrWhiteSpace(name) && !name!.Contains(';');

        if (hasId && !LooksGenerated(id!)) Add(id, null, null, []);
        if (hasName) Add(null, name, null, [], type);
        if (classes.Length > 0) Add(null, null, null, classes, type);
        if (hasName) Add(null, name, null, []);
        if (hasId && LooksGenerated(id!)) Add(id, null, null, []);
        return list;

        void Add(string? i, string? n, string? c, IReadOnlyList<string> cs, string? t = null)
        {
            var candidate = new ElementSelector(i, n, c, cs, t, 1);
            if (!list.Any(x => x.ToString() == candidate.ToString()))
                list.Add(candidate);
        }
    }
}
