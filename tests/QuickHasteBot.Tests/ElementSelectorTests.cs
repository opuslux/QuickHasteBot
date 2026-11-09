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
    [InlineData("mantine-2hi08ejzc", true)]
    [InlineData("mantine-b1omqnnct", true)]
    [InlineData("radix-:r2:", true)]
    [InlineData("ember123", true)]
    [InlineData("member1", false)]
    [InlineData("mantine", false)]
    [InlineData("field_k3x9q2mz", true)]
    [InlineData("street2", false)]
    [InlineData("h2-title", false)]
    [InlineData("utf8encoded", false)]
    [InlineData("a1b2", false)]
    [InlineData("email", false)]
    [InlineData("first-name", false)]
    [InlineData("address2", false)]
    [InlineData("1001", false)] // Control-ID in Windows-Programmen, stabil
    public void Generated_ids_are_recognized(string id, bool generated) => Assert.Equal(generated, ElementSelector.LooksGenerated(id));

    [Theory]
    [InlineData("name=Posteingang*", "Posteingang", true)]
    [InlineData("name=Posteingang*", "Posteingang - 2 ungelesene Elemente", true)]
    [InlineData("name=Posteingang*", "Gesendete Elemente", false)]
    [InlineData("name=*ungelesene*", "Posteingang - 12 ungelesene Elemente", true)]
    [InlineData("name=*.docx", "Bericht.DOCX", true)]
    [InlineData("name=*.docx", "Bericht.docx.bak", false)]
    [InlineData("name=Posteingang - * ungelesene Elemente", "Posteingang - 2 ungelesene Elemente", true)]
    [InlineData("name=Posteingang", "posteingang", true)]                       // ohne * genau, nur ohne Gross-/Kleinschreibung
    [InlineData("name=Posteingang", "Posteingang - 2 ungelesene Elemente", false)]
    [InlineData("Absenden*", "Absenden und schliessen", true)]                  // auch ohne name=
    public void Names_match_exactly_or_with_wildcards(string text, string name, bool matches) =>
        Assert.Equal(matches, Parse(text).NameMatches(name));

    [Fact]
    public void Wildcard_names_round_trip()
    {
        var selector = Parse("name=Posteingang*;type=treeitem");
        Assert.True(selector.NameIsPattern);
        Assert.Equal("name=Posteingang*;type=treeitem", selector.ToString());
        Assert.False(Parse("name=Posteingang").NameIsPattern);
    }

    [Theory]
    [InlineData("help=Drucken", "drucken", true)]
    [InlineData("tooltip=Drucken", "Drucken", true)]
    [InlineData("help=Drucken*", "Drucken (Strg+P)", true)]
    [InlineData("help=Drucken", "Drucken (Strg+P)", false)]
    public void Help_text_matches_like_the_name(string text, string help, bool matches) =>
        Assert.Equal(matches, Parse(text).HelpMatches(help));

    [Fact]
    public void Help_text_alone_is_a_selector()
    {
        var selector = Parse("help=Drucken;type=button");
        Assert.Equal("Drucken", selector.HelpText);
        Assert.Equal("help=Drucken;type=button", selector.ToString());
    }

    [Fact]
    public void Containers_come_before_the_element()
    {
        var selector = Parse("name=Postfach*;type=treeitem > name=Lieferadresse;index=2 > name=Strasse;type=edit");
        Assert.Equal("Strasse", selector.Name);
        Assert.Equal("Lieferadresse", selector.Within?.Name);
        Assert.Equal(2, selector.Within?.Index);
        Assert.Equal("Postfach*", selector.Within?.Within?.Name);
        Assert.Equal("TreeItem", selector.Within?.Within?.Type);
        Assert.Null(selector.Within?.Within?.Within);
    }

    [Fact]
    public void The_window_belongs_to_the_whole_selector_wherever_it_stands()
    {
        var selector = Parse("name=Lieferadresse;window=crm > name=Strasse");
        Assert.Equal("crm", selector.Window?.Any);
        Assert.Null(selector.Within?.Window);
        Assert.Equal("name=Lieferadresse > name=Strasse;window=crm", selector.ToString());
    }

    [Theory]
    [InlineData("name=Weiter >", "Weiter >")]   // ohne Leerzeichen danach kein Container
    [InlineData("Weiter >>", "Weiter >>")]
    [InlineData("name=a>b", "a>b")]
    public void Arrows_in_names_stay_names(string text, string name)
    {
        var selector = Parse(text);
        Assert.Equal(name, selector.Name);
        Assert.Null(selector.Within);
    }

    [Theory]
    [InlineData("name=A > ", "empty")]
    [InlineData(" > name=B", "empty")]
    [InlineData("window=crm > window=erp;name=B", "window")]
    [InlineData("name=A > color=red", "unknown selector part")]
    public void Invalid_containers_explain_why(string text, string expected)
    {
        Assert.False(ElementSelector.TryParse(text, out _, out var error));
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("name=Postfach Muster > name=Posteingang*;type=button")]
    [InlineData("#adresse > .feld;index=2")]
    [InlineData("help=Drucken;type=button")]
    public void Containers_and_help_round_trip(string text) => Assert.Equal(text, Parse(text).ToString());

    [Fact]
    public void Candidates_use_the_tooltip_after_the_name()
    {
        var candidates = ElementSelector.Candidates("", "", "", "Button", "Drucken").Select(c => c.ToString());
        Assert.Equal(["help=Drucken;type=button"], candidates);
        // gleich wie der Name: nichts Neues
        Assert.Equal(["name=OK;type=button", "name=OK"], ElementSelector.Candidates("", "OK", "", "Button", "OK").Select(c => c.ToString()));
    }

    [Theory]
    [InlineData("Strasse", true)]
    [InlineData("A;B", false)]
    [InlineData("A > B", false)]
    [InlineData(" ", false)]
    public void Only_writable_values_become_selectors(string value, bool writable) =>
        Assert.Equal(writable, ElementSelector.Writable(value));

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

public class WindowSelectorTests
{
    static WindowSelector Parse(string text)
    {
        Assert.True(WindowSelector.TryParse(text, out var selector, out var error), error);
        return selector;
    }

    [Theory]
    [InlineData("crm", "crm", "Kunden – CRM", true)]       // Programmname
    [InlineData("crm", "msedge", "Crm Handbuch – Edge", true)] // oder Titel enthält
    [InlineData("crm", "notepad", "Einkauf.txt", false)]
    [InlineData("crm.exe", "Crm", "", true)]
    [InlineData("process=crm", "msedge", "Crm Handbuch – Edge", false)] // nur der Programmname
    [InlineData("process=crm", "CRM", "", true)]
    [InlineData("title=Kunden", "crm", "Stammdaten Kunden", true)]       // ohne * genügt ein Teil
    [InlineData("title=Kunden*", "crm", "Stammdaten Kunden", false)]     // mit * muss das Muster passen
    [InlineData("title=Kunden*", "crm", "kunden – Müller AG", true)]
    [InlineData("title=*Rechnung*.pdf*", "acrobat", "Rechnung 2026-11.pdf - Adobe", true)]
    public void Windows_match_by_program_or_title(string selector, string process, string title, bool matches) =>
        Assert.Equal(matches, Parse(selector).Matches(process, title));

    [Theory]
    [InlineData("")]
    [InlineData("process=")]
    [InlineData("class=Notepad")]
    public void Invalid_window_selectors_explain(string text)
    {
        Assert.False(WindowSelector.TryParse(text, out _, out var error));
        Assert.Contains("window", error);
    }

    [Theory]
    [InlineData("crm")]
    [InlineData("process=crm")]
    [InlineData("title=Kunden*")]
    public void ToString_round_trips(string text) => Assert.Equal(text, Parse(text).ToString());

    [Fact]
    public void Element_selectors_can_name_a_window()
    {
        Assert.True(ElementSelector.TryParse("name=Suchen;type=combobox;window=process=crm", out var selector, out var error), error);
        Assert.Equal("crm", selector.Window?.Process);
        Assert.Equal("name=Suchen;type=combobox;window=process=crm", selector.ToString());
    }
}
