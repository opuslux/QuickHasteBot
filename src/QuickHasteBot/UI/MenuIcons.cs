using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;

namespace QuickHasteBot.UI;

/// <summary>
/// Symbole für die Rechtsklick- und Tray-Menüs, in der Textfarbe des Eintrags (auch ausgegraut und im dunklen Theme).
/// Meist aus Segoe Fluent Icons, Fadenkreuz und Funkelsterne wie auf den Knöpfen im Editor.
/// </summary>
internal static class MenuIcons
{
    public const string Edit = "";
    public const string OpenExternal = "";
    public const string Explorer = "";
    public const string NewSnippet = "";
    public const string NewFolder = "";
    public const string Rename = "";
    public const string Hotkey = "";
    public const string Clone = "";
    public const string Download = "";
    public const string Delete = "";
    public const string Settings = "";
    public const string Exit = "";
    public const string Keys = "";
    public const string Flow = "";
    public const string Script = "";
    public const string Search = "";
    public const string Folder = "";
    public const string Check = "";

    public const string Help = "";

    static readonly FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    // Dieselben Formen wie im Editor (SnippetEditorWindow.xaml)
    static readonly Geometry CrosshairShape = Freeze(Geometry.Parse(
        "M16,0 L16,11 M16,21 L16,32 M0,16 L11,16 M21,16 L32,16 M6,16 A10,10 0 1 1 26,16 A10,10 0 1 1 6,16"));
    static readonly Geometry SparkleShape = Freeze(Geometry.Parse(
        "M7,4 Q7,10 13,10 Q7,10 7,16 Q7,10 1,10 Q7,10 7,4 Z M13,0 Q13,3 16,3 Q13,3 13,6 Q13,3 10,3 Q13,3 13,0 Z M3,1 Q3,3 5,3 Q3,3 3,5 Q3,3 1,3 Q3,3 3,1 Z"));

    /// <summary>Ein Zeichen aus Segoe Fluent Icons. Die Farbe erbt es vom Menüeintrag.</summary>
    public static UIElement Glyph(string glyph) => new TextBlock
    {
        Text = glyph,
        FontFamily = SymbolFont,
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static UIElement Crosshair() => PathIcon(CrosshairShape, Shape.StrokeProperty, strokeThickness: 1.4);

    public static UIElement Sparkle() => PathIcon(SparkleShape, Shape.FillProperty, strokeThickness: 0);

    /// <summary>Eintrag mit Symbol und Aktion.</summary>
    public static MenuItem Item(string header, UIElement icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = icon };
        item.Click += (_, _) => action();
        return item;
    }

    static ShapePath PathIcon(Geometry geometry, DependencyProperty colored, double strokeThickness)
    {
        var path = new ShapePath
        {
            Data = geometry,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            StrokeThickness = strokeThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Die vererbte Textfarbe, wie bei den Glyphen. MenuItem.Foreground selbst setzt das Fluent-Theme
        // nur für ausgegraute Einträge, sonst blieben die Formen unsichtbar.
        path.SetBinding(colored, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
        return path;
    }

    static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
