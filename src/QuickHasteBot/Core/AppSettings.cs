namespace QuickHasteBot.Core;

public enum PasteMode
{
    /// <summary>Text ins Clipboard legen und Strg+V senden, danach das alte Clipboard wiederherstellen.</summary>
    Clipboard,

    /// <summary>Text Zeichen für Zeichen als Unicode-Tastatureingabe senden.</summary>
    Typing,
}

/// <summary>Wo der Mauszeiger beim Öffnen im Popup sitzt, angeordnet wie der Ziffernblock.</summary>
public enum PopupPosition
{
    BottomLeft = 1,
    Bottom,
    BottomRight,
    Left,
    Center,
    Right,
    TopLeft,
    Top,
    TopRight,
}

/// <summary>Hell oder dunkel, System folgt der App-Einstellung von Windows (auch beim Umschalten im Betrieb).</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public const string DefaultStructurePath = "structure";

    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    public string StructurePath { get; set; } = DefaultStructurePath;

    public PasteMode PasteMode { get; set; } = PasteMode.Clipboard;

    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Ein Klick im Popup fügt ein bzw. öffnet den Ordner, sonst wählt er nur aus und es braucht einen Doppelklick.</summary>
    public bool SingleClick { get; set; } = true;

    /// <summary>Suche auch im Inhalt der Textbausteine, nicht nur im Namen.</summary>
    public bool SearchContent { get; set; } = true;

    /// <summary>{{ … }} in Textbausteinen beim Einfügen als JavaScript auswerten. Aus: wird wörtlich eingefügt (z.B. Mustache-Vorlagen).</summary>
    public bool EvaluateScripts { get; set; } = true;

    /// <summary>Tasten-Tags wie {tab} und {enter} in Textbausteinen ausführen (Formulare ausfüllen). Aus: wird wörtlich eingefügt.</summary>
    public bool EvaluateKeyTags { get; set; } = true;

    /// <summary>Formular-Bausteine abbrechen, wenn ein anderes Fenster nach vorne kommt. Pro Baustein mit {guard,off} abschaltbar.</summary>
    public bool KeyTagsWindowGuard { get; set; } = true;

    /// <summary>"Bearbeiten" öffnet Textbausteine im eingebauten Editor. Aus: mit dem Programm, das Windows für die Endung vorsieht.</summary>
    public bool UseBuiltInEditor { get; set; } = true;

    /// <summary>Element-Picker: {window,process=…} vor den Tag setzen, damit der Baustein das Programm zuerst nach vorne holt.</summary>
    public bool PickerIncludeWindow { get; set; }

    /// <summary>Wohin das KI-Icon im Popup den Prompt schickt, mit {prompt} und {lang}. Standard: ChatGPT.</summary>
    public string AiPromptUrl { get; set; } = ScriptPrompt.DefaultUrl;

    /// <summary>Zuletzt im KI-Prompt-Fenster eingegebener Text, steht beim nächsten Öffnen wieder da.</summary>
    public string AiPromptText { get; set; } = "";

    /// <summary>Diese Dateien werden eingefügt, alle anderen mit der zugeordneten Anwendung geöffnet.</summary>
    public List<string> TextExtensions { get; set; } = [".txt"];

    /// <summary>"en" oder "de", leer heisst: wie die Anzeigesprache von Windows.</summary>
    public string Language { get; set; } = "";

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Breite des Popups, an den Rändern verstellbar. Schmaler als die Zeile mit den Tastenkürzeln wird es nie.</summary>
    public double PopupWidth { get; set; } = DefaultPopupWidth;

    public const double DefaultPopupWidth = 720;

    /// <summary>Anteil der Liste links an der Breite des Popups, der Rest gehört dem Inhalt rechts. Wird mit dem Trenner verschoben.</summary>
    public double ListColumnShare { get; set; } = DefaultListColumnShare;

    public const double DefaultListColumnShare = 1.0 / 3;

    /// <summary>Standard oben rechts: Das Popup geht links unterhalb der Maus auf.</summary>
    public PopupPosition PopupPosition { get; set; } = PopupPosition.TopRight;

    /// <summary>Lange Zeilen in der Textvorschau umbrechen statt horizontal scrollen.</summary>
    public bool WrapPreview { get; set; }

    /// <summary>Das Popup öffnet im zuletzt angezeigten Ordner statt oben, mit dem damals markierten Eintrag.</summary>
    public bool RememberFolder { get; set; }

    /// <summary>Ordner beim letzten Schliessen, relativ zum structure-Ordner. Gilt nur mit <see cref="RememberFolder"/>.</summary>
    public string LastFolder { get; set; } = "";

    /// <summary>Der dabei markierte Eintrag, relativ zum structure-Ordner.</summary>
    public string LastSelected { get; set; } = "";

    /// <summary>Das Popup öffnet mit dem Suchtext vom letzten Schliessen, Enter startet wieder denselben Eintrag.</summary>
    public bool RememberQuery { get; set; }

    /// <summary>Suchtext beim letzten Schliessen. Gilt nur mit <see cref="RememberQuery"/>.</summary>
    public string LastQuery { get; set; } = "";

    /// <summary>Auch von Hand eingetragene Werte lassen beide Spalten sichtbar, Unsinn ergibt den Standard.</summary>
    public static double ClampColumnShare(double share) =>
        double.IsFinite(share) ? Math.Clamp(share, 0.15, 0.85) : DefaultListColumnShare;

    /// <summary>
    /// Stelle der Maus als Anteil von Breite und Höhe des Popups: 0 links/oben, 0.5 Mitte, 1 rechts/unten.
    /// Eine von Hand eingetragene Zahl ausserhalb von 1 bis 9 gilt als Standard.
    /// </summary>
    public static (double X, double Y) PopupFractions(PopupPosition position)
    {
        var n = Enum.IsDefined(position) ? (int)position : (int)PopupPosition.TopRight;
        return ((n - 1) % 3 / 2.0, (2 - (n - 1) / 3) / 2.0);
    }

    public static PasteMode Other(PasteMode mode) => mode == PasteMode.Clipboard ? PasteMode.Typing : PasteMode.Clipboard;
}
