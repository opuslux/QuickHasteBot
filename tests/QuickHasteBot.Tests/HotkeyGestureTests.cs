using System.Windows.Input;
using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Y", "Ctrl+Shift+Y")]
    [InlineData("shift + ctrl + y", "Ctrl+Shift+Y")]
    [InlineData("Strg+Alt+1", "Ctrl+Alt+1")]
    [InlineData("Win+Space", "Win+Space")]
    [InlineData("MOD_CONTROL+MOD_SHIFT+Y", "Ctrl+Shift+Y")]
    [InlineData("F13", "F13")]
    public void Parses_and_formats(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(expected, gesture.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Y")]                // ohne Modifier wäre die Taste global blockiert
    [InlineData("Ctrl+Shift")]       // keine eigentliche Taste
    [InlineData("Ctrl+Y+X")]         // zwei Tasten
    [InlineData("Ctrl+Bogus")]
    [InlineData("Ctrl+42")]          // Zahlen sind keine Enum-Werte
    public void Rejects_invalid(string input) => Assert.False(HotkeyGesture.TryParse(input, out _));

    [Fact]
    public void Single_digit_means_number_key()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+5", out var gesture));
        Assert.Equal(Key.D5, gesture.Key);
    }

    [Fact]
    public void Default_is_ctrl_shift_y_like_the_old_version()
    {
        Assert.Equal("Ctrl+Shift+Y", HotkeyGesture.Default.ToString());
        Assert.Equal(0x59, HotkeyGesture.Default.VirtualKey);
        Assert.Equal(0x2 | 0x4, (int)HotkeyGesture.Default.Modifiers); // MOD_CONTROL | MOD_SHIFT
    }
}
