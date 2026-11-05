using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Roter Rahmen um das Element unter dem Fadenkreuz. Für Maus und UI Automation durchsichtig (WS_EX_TRANSPARENT),
/// sonst fände der Picker statt des Elements den Rahmen. Positioniert in physischen Pixeln wie UI Automation sie liefert.
/// </summary>
internal sealed class ElementHighlight : Window
{
    const int Thickness = 2;

    public ElementHighlight()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Content = new Border
        {
            BorderThickness = new Thickness(Thickness),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)),
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        };
    }

    public void ShowAt(Rect bounds)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || double.IsInfinity(bounds.Width))
        {
            Hide();
            return;
        }
        if (!IsVisible)
        {
            (Left, Top, Width, Height) = (-32000, -32000, 1, 1);
            Show();
        }
        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST,
            (int)bounds.X - Thickness, (int)bounds.Y - Thickness, (int)bounds.Width + 2 * Thickness, (int)bounds.Height + 2 * Thickness,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
