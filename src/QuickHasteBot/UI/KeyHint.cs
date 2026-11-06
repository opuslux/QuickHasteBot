using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickHasteBot.UI;

/// <summary>
/// Ein Eintrag der Kürzel-Leiste im Popup: jede Taste als Kästchen wie auf der Tastatur, dahinter die Aktion.
/// <see cref="Keys"/> nennt die Tasten sprachneutral, durch Leerzeichen getrennt ("Ctrl O", "Shift Enter").
/// Ctrl, Shift und Enter werden zu Strg/Ctrl, ⇧ und ↵, alles andere steht so im Kästchen.
/// Mit <see cref="IsLink"/> lässt sich der Eintrag anklicken und färbt sich beim Darüberfahren wie ein Link.
/// </summary>
internal sealed class KeyHint : StackPanel
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(string), typeof(KeyHint), new PropertyMetadata("", (d, _) => ((KeyHint)d).Build()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(KeyHint), new PropertyMetadata("", (d, e) => ((KeyHint)d)._label.Text = (string)e.NewValue));

    public static readonly DependencyProperty IsLinkProperty = DependencyProperty.Register(
        nameof(IsLink), typeof(bool), typeof(KeyHint), new PropertyMetadata(false, (d, _) => ((KeyHint)d).UpdateLink()));

    readonly TextBlock _label = new() { FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    public KeyHint()
    {
        Orientation = Orientation.Horizontal;
        // Sonst zählen die Lücken zwischen den Kästchen nicht zur Klickfläche
        Background = Brushes.Transparent;
        Build();
    }

    public string Keys
    {
        get => (string)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsLink
    {
        get => (bool)GetValue(IsLinkProperty);
        set => SetValue(IsLinkProperty, value);
    }

    static readonly FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Was im Kästchen steht, z.B. "Strg" auf Deutsch. Shift und Enter als Symbol, die Pfeile der Textschrift sind zu klein.</summary>
    internal static (string Text, bool IsSymbol) KeyText(string key) => key switch
    {
        "Ctrl" => (Strings.KeyCtrl, false),
        "Shift" => ("", true),
        "Enter" => ("", true),
        _ => (key, false),
    };

    void Build()
    {
        Children.Clear();
        foreach (var key in Keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Children.Add(KeyCap(KeyText(key)));
        Children.Add(_label);
        UpdateLink();
    }

    static Border KeyCap((string Text, bool IsSymbol) key)
    {
        var caption = new TextBlock { Text = key.Text, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (key.IsSymbol)
        {
            caption.FontFamily = SymbolFont;
            caption.Margin = new Thickness(0, 1, 0, 0);
        }
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        // Unten etwas dicker, wie eine Taste
        var cap = new Border
        {
            Child = caption,
            MinWidth = 16,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 0, 2, 0),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1, 1, 1, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        return cap;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty)
            UpdateLink();
    }

    void UpdateLink()
    {
        var hot = IsLink && IsMouseOver;
        Cursor = IsLink ? System.Windows.Input.Cursors.Hand : null;
        _label.TextDecorations = hot ? TextDecorations.Underline : null;
        _label.SetResourceReference(TextBlock.ForegroundProperty, hot ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
    }
}
