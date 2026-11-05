using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Kärtchen, das dem Mauszeiger folgt, solange der Editor auf den Klick ins Zielfeld wartet: dort schaut man hin,
/// nicht auf die Statuszeile. Für Maus und Fokus durchlässig, der Klick landet also im Feld darunter, und das
/// Kärtchen wird nie selbst zum Ziel.
/// </summary>
internal sealed class CursorHint : Window
{
    const int Offset = 20; // physische Pixel rechts unterhalb der Zeigerspitze
    const int ShadowMargin = 8;

    readonly DispatcherTimer _follow;

    /// <param name="title">Fett, z.B. "Ins Zielfeld klicken".</param>
    /// <param name="code">Was läuft, in Code-Schrift. Null: keine Zeile dafür.</param>
    /// <param name="note">Klein darunter, z.B. "Esc bricht ab".</param>
    public CursorHint(string title, string? code, string note)
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

        var panel = new StackPanel();
        panel.Children.Add(Text(title, 14, FontWeights.SemiBold));
        if (code is not null)
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(code, 12, FontWeights.Normal, new FontFamily("Cascadia Mono, Consolas")),
            };
            panel.Children.Add(chip);
        }
        var small = Text(note, 11, FontWeights.Normal);
        small.Margin = new Thickness(0, 6, 0, 0);
        small.Opacity = 0.85;
        panel.Children.Add(small);

        var card = new Border
        {
            Margin = new Thickness(ShadowMargin),
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
            MaxWidth = 420,
            Child = panel,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        card.SetResourceReference(TextElement.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        };
        _follow = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Follow(), Dispatcher) { IsEnabled = false };
        Closed += (_, _) => _follow.Stop();
    }

    static TextBlock Text(string text, double size, FontWeight weight, FontFamily? family = null)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        if (family is not null)
        {
            block.FontFamily = family;
            block.TextWrapping = TextWrapping.NoWrap;
            block.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        return block;
    }

    /// <summary>Zeigt das Kärtchen am Mauszeiger, ohne den Fokus zu nehmen.</summary>
    public void ShowAtCursor()
    {
        Left = -32000;
        Top = -32000;
        Show();
        Follow();
        _follow.Start();
    }

    /// <summary>Rechts unterhalb des Zeigers, am Bildschirmrand auf die andere Seite.</summary>
    void Follow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || !GetCursorPos(out var cursor) || !GetWindowRect(hwnd, out var rect)) return;
        var (width, height) = (rect.Right - rect.Left, rect.Bottom - rect.Top);

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.rcWork;

        var x = cursor.X + Offset - ShadowMargin;
        var y = cursor.Y + Offset - ShadowMargin;
        if (x + width > work.Right) x = cursor.X - width - Offset / 2 + ShadowMargin;
        if (y + height > work.Bottom) y = cursor.Y - height - Offset / 2 + ShadowMargin;
        SetWindowPos(hwnd, HWND_TOPMOST, Math.Max(x, work.Left), Math.Max(y, work.Top), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
