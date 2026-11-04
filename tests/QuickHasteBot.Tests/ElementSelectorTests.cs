using QuickHasteBot.Core;

namespace QuickHasteBot.Tests;

public class ElementSelectorTests
{
    static ElementSelector Parse(string text)
    {
        Assert.True(ElementSelector.TryParse(text, out var selector, out var error), error);
        return selector;
    }

    [Fact]
    public void Hash_is_the_id() => Assert.Equal("email", Parse("#email").Id);

    [Fact]
    public void Dots_are_classes_that_must_all_be_present() => Assert.Equal(["btn", "primary"], Parse(".btn.primary").Classes);

    [Fact]
    public void Plain_text_is_the_name() => Assert.Equal("Absenden", Parse("Absenden").Name);

    [Fact]
    public void Parts_combine_with_semicolons()
    {
        var selector = Parse("name=E-Mail Adresse; type=edit; index=2");
        Assert.Equal("E-Mail Adresse", selector.Name);
        Assert.Equal("Edit", selector.Type);
        Assert.Equal(2, selector.Index);
        Assert.Null(selector.Id);
    }

    [Theory]
    [InlineData("type=dropdown", "ComboBox")]
    [InlineData("type=select", "ComboBox")]
    [InlineData("type=input", "Edit")]
    [InlineData("type=link", "Hyperlink")]
    [InlineData("type=Button", "Button")]
    public void Types_have_friendly_aliases(string text, string uiaType) => Assert.Equal(uiaType, Parse(text).Type);

    [Fact]
    public void Class_is_the_whole_attribute() => Assert.Equal("btn primary", Parse("class=btn primary").ClassName);

    [Theory]
    [InlineData("", "empty")]
    [InlineData("type=banana", "unknown element type")]
    [InlineData("index=0", "index")]
    [InlineData("color=red", "unknown selector part")]
    public void Invalid_selectors_explain_why(string text, string expected)
    {
        Assert.False(ElementSelector.TryParse(text, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("#email")]
    [InlineData(".btn.primary;type=button")]
    [InlineData("name=Absenden;type=button")]
    [InlineData("name=Feld;type=edit;index=3")]
    [InlineData("class=btn primary")]
    public void ToString_round_trips(string text) => Assert.Equal(text, Parse(text).ToString());

    [Theory]
    [InlineData(":r3:", true)]
    [InlineData(":R1ab:", true)]
    [InlineData("mui-12345", true)]
    [InlineData("input_1699457", true)]
    [InlineData("3f2a9c1e-7b4d-4e2a-9c1e-7b4d4e2a9c1e", true)]
    [InlineData("email", false)]
    [InlineData("first-name", false)]
    [InlineData("address2", false)]
    [InlineData("1001", false)] // Control-ID in Windows-Programmen, stabil
    public void Generated_ids_are_recognized(string id, bool generated) => Assert.Equal(generated, ElementSelector.LooksGenerated(id));

    [Fact]
    public void Candidates_prefer_a_real_id_then_name_and_type_then_classes()
    {
        var candidates = ElementSelector.Candidates("email", "E-Mail", "feld gross", "Edit").Select(c => c.ToString());
        Assert.Equal(["#email", "name=E-Mail;type=edit", ".feld.gross;type=edit", "name=E-Mail"], candidates);
    }

    [Fact]
    public void Generated_ids_come_last()
    {
        var candidates = ElementSelector.Candidates(":r3:", "Absenden", "", "Button").Select(c => c.ToString());
        Assert.Equal(["name=Absenden;type=button", "name=Absenden", "#:r3:"], candidates);
    }
}
