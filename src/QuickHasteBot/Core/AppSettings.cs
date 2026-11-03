namespace QuickHasteBot.Core;

public enum PasteMode
{
    /// <summary>Text ins Clipboard legen und Strg+V senden, danach das alte Clipboard wiederherstellen.</summary>
    Clipboard,

    /// <summary>Text Zeichen für Zeichen als Unicode-Tastatureingabe senden.</summary>
    Typing,
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

    /// <summary>Diese Dateien werden eingefügt, alle anderen mit der zugeordneten Anwendung geöffnet.</summary>
    public List<string> TextExtensions { get; set; } = [".txt"];

    /// <summary>"en" oder "de", leer heisst: wie die Anzeigesprache von Windows.</summary>
    public string Language { get; set; } = "";

    /// <summary>Breite des Popups, an den Rändern verstellbar. Schmaler als die Zeile mit den Tastenkürzeln wird es nie.</summary>
    public double PopupWidth { get; set; } = DefaultPopupWidth;

    public const double DefaultPopupWidth = 720;

    /// <summary>Anteil der Liste links an der Breite des Popups, der Rest gehört dem Inhalt rechts. Wird mit dem Trenner verschoben.</summary>
    public double ListColumnShare { get; set; } = DefaultListColumnShare;

    public const double DefaultListColumnShare = 1.0 / 3;

    /// <summary>Lange Zeilen in der Textvorschau umbrechen statt horizontal scrollen.</summary>
    public bool WrapPreview { get; set; }

    /// <summary>Das Popup öffnet im zuletzt angezeigten Ordner statt oben, mit dem damals markierten Eintrag.</summary>
    public bool RememberFolder { get; set; }

    /// <summary>Ordner beim letzten Schliessen, relativ zum structure-Ordner. Gilt nur mit <see cref="RememberFolder"/>.</summary>
    public string LastFolder { get; set; } = "";

    /// <summary>Der dabei markierte Eintrag, relativ zum structure-Ordner.</summary>
    public string LastSelected { get; set; } = "";

    /// <summary>Auch von Hand eingetragene Werte lassen beide Spalten sichtbar, Unsinn ergibt den Standard.</summary>
    public static double ClampColumnShare(double share) =>
        double.IsFinite(share) ? Math.Clamp(share, 0.15, 0.85) : DefaultListColumnShare;

    public static PasteMode Other(PasteMode mode) => mode == PasteMode.Clipboard ? PasteMode.Typing : PasteMode.Clipboard;
}
