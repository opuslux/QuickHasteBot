using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Leiste unten mittig am Zielfenster, für den Schritt-für-Schritt-Lauf und die Aufnahme. Wird nie aktiv
/// (WS_EX_NOACTIVATE, ein Klick beantwortet WM_MOUSEACTIVATE mit MA_NOACTIVATE), damit Tasten und Fokus im Zielfenster
/// bleiben. Am Griff links verschiebbar, danach bleibt sie, wo man sie hingezogen hat.
/// </summary>
internal abstract class FloatingBar : Window
{
    /// <summary>Abstand zum unteren Rand des Zielfensters, physische Pixel.</summary>
    const int BottomMargin = 24;
    const int ShadowMargin = 8;

    bool _moved;
    POINT _dragStart;
    RECT _dragWindow;

    protected FloatingBar(string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Title = title;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
    }

    /// <summary>Der Inhalt in einer Karte mit Schatten, links der Griff zum Verschieben.</summary>
    protected void SetBody(UIElement body)
    {
        var grip = new TextBlock
        {
            Text = "⋮⋮", FontSize = 14, Opacity = 0.6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.SizeAll, ToolTip = Strings.StepBarDrag,
        };
        var row = new DockPanel { Children = { grip, body } };

        var card = new Border
        {
            Margin = new Thickness(ShadowMargin),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinWidth = 420,
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 },
            Cursor = Cursors.SizeAll,
        };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        card.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        card.MouseLeftButtonDown += StartDrag;
        card.MouseMove += Drag;
        card.MouseLeftButtonUp += (_, _) => card.ReleaseMouseCapture();
        Content = card;
    }

    /// <summary>Vor dem ersten Mal nicht sichtbar, ohne das Zielfenster zu deaktivieren.</summary>
    public void ShowAt(nint window)
    {
        if (!IsVisible)
        {
            (Left, Top) = (-32000, -32000);
            Show();
        }
        UpdateLayout();
        Place(window);
    }

    /// <summary>Unten mittig ans Fenster, ausser man hat die Leiste selbst verschoben.</summary>
    public void Place(nint window)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_moved || hwnd == 0) return;
        var root = window == 0 ? 0 : GetAncestor(window, GA_ROOT);
        if (root == 0 || !GetWindowRect(root, out var target) || !GetWindowRect(hwnd, out var bar)) return;

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(root, MONITOR_DEFAULTTONEAREST), ref info);
        var (x, y) = Position(target, info.rcWork, bar.Right - bar.Left, bar.Bottom - bar.Top);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Unten mittig im Zielfenster, aber immer ganz im Arbeitsbereich des Bildschirms.</summary>
    internal static (int X, int Y) Position(RECT target, RECT work, int width, int height)
    {
        var x = (target.Left + target.Right) / 2 - width / 2;
        var y = Math.Min(target.Bottom, work.Bottom) - height - BottomMargin + ShadowMargin;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return (x, y);
    }

    /// <summary>Ein Klick in die Leiste aktiviert sie nicht, das Zielfenster bleibt vorne.</summary>
    internal static nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_MOUSEACTIVATE) return 0;
        handled = true;
        return MA_NOACTIVATE;
    }

    void StartDrag(object sender, MouseButtonEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetCursorPos(out _dragStart) || !GetWindowRect(hwnd, out _dragWindow)) return;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void Drag(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured || !GetCursorPos(out var cursor)) return;
        _moved = true;
        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST,
            _dragWindow.Left + cursor.X - _dragStart.X, _dragWindow.Top + cursor.Y - _dragStart.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Ein Knopf, der nie den Fokus nimmt.</summary>
    protected static Button BarButton(string text, Action click, bool accent = false)
    {
        var button = new Button { Content = text, Focusable = false, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 4), Cursor = Cursors.Arrow };
        if (accent)
            button.SetResourceReference(StyleProperty, "AccentButtonStyle");
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Ein Schritt in Code-Schrift, zu lange Zeilen mit …</summary>
    protected static TextBlock Code() => new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Margin = new Thickness(0, 2, 0, 2),
        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left,
    };
}
