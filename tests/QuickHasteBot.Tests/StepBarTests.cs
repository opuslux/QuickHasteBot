using QuickHasteBot.Core;
using QuickHasteBot.UI;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Tests;

/// <summary>Position, Schritt-Texte und der Klick-Hook der Schritt-Leiste, ohne Fenster.</summary>
public class StepBarTests
{
    static RECT Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    static readonly RECT Work = Rect(0, 0, 1920, 1040);

    [Fact]
    public void It_sits_at_the_bottom_center_of_the_target_window()
    {
        var (x, y) = StepBar.Position(Rect(100, 100, 1100, 800), Work, 400, 100);
        Assert.Equal(400, x); // Mitte 600, halbe Breite 200
        Assert.True(y + 100 <= 800 && y + 100 > 800 - 40, $"y={y}");
    }

    [Fact]
    public void It_stays_on_the_screen_when_the_window_is_partly_off_it()
    {
        // Fenster ragt links und unten über den Bildschirm hinaus
        var (x, y) = StepBar.Position(Rect(-900, 600, 300, 1400), Work, 600, 100);
        Assert.Equal(0, x);
        Assert.True(y + 100 <= Work.Bottom, $"y={y}");

        // Fenster kleiner als die Leiste, ganz rechts
        (x, _) = StepBar.Position(Rect(1800, 100, 1920, 200), Work, 600, 100);
        Assert.Equal(1920 - 600, x);
    }

    [Fact]
    public void Steps_read_like_the_snippet_on_one_line()
    {
        Assert.Equal("{tab}", StepBar.StepText(Assert.IsType<KeyStep>(Single("{tab}")), paste: false));
        Assert.Equal("{focus,#email}", StepBar.StepText(Single("{focus,#email}"), paste: false));
        Assert.Equal("«MfG⏎Max»", StepBar.StepText(new TextStep("MfG\r\nMax"), paste: false));

        var shortened = StepBar.StepText(new TextStep(new string('x', 200)), paste: false);
        Assert.True(shortened.Length <= 70, shortened);
        Assert.EndsWith("…»", shortened);
    }

    [Fact]
    public void A_click_never_activates_the_bar()
    {
        var handled = false;
        Assert.Equal(MA_NOACTIVATE, StepBar.Hook(0, WM_MOUSEACTIVATE, 0, 0, ref handled));
        Assert.True(handled);

        handled = false;
        Assert.Equal(0, StepBar.Hook(0, 0x0201 /* WM_LBUTTONDOWN */, 0, 0, ref handled));
        Assert.False(handled);
    }

    static FormStep Single(string tag)
    {
        Assert.True(KeyTags.TryParse(tag.Trim('{', '}'), null, out var steps, out _));
        return Assert.Single(steps);
    }
}
