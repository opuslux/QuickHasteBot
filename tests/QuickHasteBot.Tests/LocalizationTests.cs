using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using QuickHasteBot.UI;

namespace QuickHasteBot.Tests;

/// <summary>Jeder Text muss in Strings.resx (Englisch) und Strings.de.resx stehen, mit denselben Platzhaltern.</summary>
public class LocalizationTests
{
    static Dictionary<string, string> Texts(string culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"Keine Texte für '{culture}'");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void German_and_english_have_the_same_texts()
    {
        var english = Texts("");
        var german = Texts("de");

        Assert.Empty(english.Keys.Except(german.Keys));
        Assert.Empty(german.Keys.Except(english.Keys));
        Assert.All(german, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key));
    }

    [Fact]
    public void Placeholders_match()
    {
        var english = Texts("");
        var german = Texts("de");

        foreach (var (key, text) in english)
            Assert.True(Placeholders(text).SetEquals(Placeholders(german[key])), key);
    }

    [Fact]
    public void Every_entry_in_the_tag_menu_has_a_description_and_help()
    {
        var english = Texts("");
        var german = Texts("de");
        var entries = TagCatalog.Groups().SelectMany(g => g.Entries).OfType<TagEntry>().ToList();

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
            foreach (var key in new[] { entry.Description, entry.Help })
            {
                Assert.True(english.TryGetValue(key, out var en) && en.Length > 0, $"{entry.Tag}: {key} (en)");
                Assert.True(german.TryGetValue(key, out var de) && de.Length > 0, $"{entry.Tag}: {key} (de)");
            }
    }

    [Fact]
    public void Every_help_topic_in_the_editor_has_its_texts()
    {
        var english = Texts("");
        var german = Texts("de");
        foreach (var key in Core.TagHelp.AllKeys)
            foreach (var name in new[] { key, key + "Help" })
            {
                Assert.True(english.ContainsKey(name), name + " (en)");
                Assert.True(german.ContainsKey(name), name + " (de)");
            }
    }

    [Fact]
    public void Unknown_language_falls_back_to_english() =>
        Assert.Equal("Snippets", Strings.ResourceManager.GetString(nameof(Strings.BreadcrumbRoot), CultureInfo.GetCultureInfo("fr")));

    static HashSet<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();
}
