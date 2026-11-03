using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>
/// Wo der Mauszeiger beim Öffnen im Popup sitzt: ein Mini-Popup mit 3×3 Feldern wie der Ziffernblock.
/// Anklicken oder mit Fokus 1–9 bzw. die Pfeiltasten drücken.
/// </summary>
internal partial class PopupPositionPicker : UserControl
{
    static readonly Duration PointerAnimation = TimeSpan.FromMilliseconds(160);

    PopupPosition _position;

    public PopupPositionPicker()
    {
        InitializeComponent();
        Position = PopupPosition.TopRight;
    }

    /// <summary>Kommt nur bei Änderungen von Hand, nicht beim Setzen von <see cref="Position"/>.</summary>
    public event EventHandler<PopupPosition>? PositionChanged;

    public PopupPosition Position
    {
        get => _position;
        set
        {
            _position = Enum.IsDefined(value) ? value : PopupPosition.TopRight;
            Cells.First(c => (PopupPosition)c.Tag == _position).IsChecked = true;
            MovePointer(animate: IsLoaded);
        }
    }

    IEnumerable<RadioButton> Cells => ((UniformGrid)Frame.Child).Children.OfType<RadioButton>();

    void Select(PopupPosition position)
    {
        if (position == _position) return;
        Position = position;
        PositionChanged?.Invoke(this, position);
    }

    void OnCellChecked(object sender, RoutedEventArgs e) => Select((PopupPosition)((RadioButton)sender).Tag);

    // Die Felder selbst sind nicht fokussierbar, nach einem Klick geht die Tastatur ans Raster
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var n = (int)_position;
        int? next = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            // Am Rand bleibt es stehen, der Fokus soll nicht wegspringen
            Key.Up => n <= 6 ? n + 3 : n,
            Key.Down => n >= 4 ? n - 3 : n,
            Key.Left => (n - 1) % 3 > 0 ? n - 1 : n,
            Key.Right => (n - 1) % 3 < 2 ? n + 1 : n,
            _ => null,
        };
        if (next is not { } number)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        Select((PopupPosition)number);
    }

    void MovePointer(bool animate)
    {
        var (x, y) = AppSettings.PopupFractions(_position);
        Move(Canvas.LeftProperty, x * Frame.Width, animate);
        Move(Canvas.TopProperty, y * Frame.Height, animate);
    }

    void Move(DependencyProperty property, double value, bool animate)
    {
        if (animate)
        {
            Pointer.BeginAnimation(property, new DoubleAnimation(value, PointerAnimation) { EasingFunction = new CubicEase() });
            return;
        }
        Pointer.BeginAnimation(property, null);
        Pointer.SetValue(property, value);
    }
}
