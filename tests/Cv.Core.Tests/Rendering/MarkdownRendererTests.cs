using Cv.Core.Drafts;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Core.Rendering;
using Cv.Testing;

namespace Cv.Core.Tests.Rendering;

public class MarkdownRendererTests
{
    private static readonly CvCatalog Catalog = TestDocuments.Catalog;

    private static string Render(TestDraft draft, string locale) =>
        MarkdownRenderer.Render(Localizer.Localize(DraftBuilder.Build(draft.ToDraft(), null, Catalog).Document, Catalog, locale));

    [Fact]
    public void The_sample_renders_as_header_then_sections()
    {
        var md = Render(TestDraft.Sample().Draft, "en");

        Assert.Equal(
            """
            # Ada Lovelace

            Software engineer

            [ada@example.com](<mailto:ada@example.com>)

            ## Experience

            **Analytical Engines Ltd, London**

            ### Lead engineer | Mar 2021 – Present

            - Built the difference engine

            """.ReplaceLineEndings("\n"),
            md);
    }

    [Theory]
    [InlineData("en", "### Lead engineer | Mar 2021 – Present")]
    [InlineData("nb", "### Ledende utvikler | mars 2021 – nå")]
    [InlineData("fr", "### Ingénieure principale | mars 2021 – aujourd’hui")]
    public void Entries_use_the_locale_s_text_and_dates(string locale, string expected) =>
        Assert.Contains(expected + "\n", Render(TestDraft.Sample().Draft, locale));

    [Fact]
    public void An_entry_shows_organisation_and_location_above_title_and_dates()
    {
        var d = new TestDraft();
        var job = d.Add(d.Add(d.Root, "section", en: "Experience"), "entry", en: "Frontend Lead",
            attrs: new Dictionary<string, string> { ["start"] = "2026-05" });
        d.Add(job, "bullet", en: "Led the team");
        d.Add(job, "location", en: "Haugesund, Norway");
        d.Add(job, "subtitle", zxx: "Norwegian Maritime Authority");

        var md = Render(d, "en");

        Assert.Contains(
            "**Norwegian Maritime Authority** | Haugesund, Norway\n\n### Frontend Lead | May 2026 – Present\n\n- Led the team\n",
            md);
    }

    [Fact]
    public void An_entry_without_title_still_shows_its_dates()
    {
        var d = new TestDraft();
        d.Add(d.Add(d.Root, "section", en: "Education"), "entry", attrs: new Dictionary<string, string> { ["end"] = "2020" });

        Assert.Equal("## Education\n\n### 2020\n", Render(d, "en"));
    }

    [Fact]
    public void Contacts_share_one_line_and_link_only_safe_schemes()
    {
        var d = new TestDraft();
        d.Add(d.Root, "contact", zxx: "ada@example.com", attrs: new Dictionary<string, string> { ["kind"] = "email" });
        d.Add(d.Root, "contact", zxx: "+47 912 34 567", attrs: new Dictionary<string, string> { ["kind"] = "phone" });
        d.Add(d.Root, "contact", zxx: "example.com/ada_(cv)", attrs: new Dictionary<string, string> { ["kind"] = "url" });
        d.Add(d.Root, "contact", zxx: "javascript:alert(1)", attrs: new Dictionary<string, string> { ["kind"] = "url" });
        d.Add(d.Root, "contact", zxx: "Oslo", attrs: new Dictionary<string, string> { ["kind"] = "location" });

        var md = Render(d, "en");

        Assert.Equal(
            "[ada@example.com](<mailto:ada@example.com>) | [+47 912 34 567](<tel:+4791234567>) | " +
            "[example.com/ada\\_(cv)](<https://example.com/ada_(cv)>) | javascript:alert(1) | Oslo\n",
            md);
    }

    [Theory]
    [InlineData("en", "**Languages:** C#, TypeScript")]
    [InlineData("fr", "**Langues :** C#, TypeScript")] // narrow no-break space before the colon
    public void A_skill_group_is_one_labelled_comma_separated_line(string locale, string expected)
    {
        var d = new TestDraft();
        var group = d.Add(d.Add(d.Root, "section", en: "Skills", fr: "Compétences"), "skill_group", en: "Languages", fr: "Langues");
        d.Add(group, "skill", zxx: "C#");
        d.Add(group, "skill", zxx: "TypeScript");

        Assert.Contains(expected + "\n", Render(d, locale));
    }

    [Fact]
    public void Loose_skills_are_one_comma_separated_line()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Tools");
        d.Add(section, "skill", zxx: "Git");
        d.Add(section, "skill", zxx: "Docker");

        Assert.Equal("## Tools\n\nGit, Docker\n", Render(d, "en"));
    }

    [Fact]
    public void French_high_punctuation_gets_a_narrow_no_break_space()
    {
        var d = new TestDraft();
        d.Add(d.Add(d.Root, "section", en: "Note", fr: "Note"), "paragraph", en: "Hi!", fr: "Oui ! À 10:30 ?");

        const char nnbsp = Typography.NarrowNoBreakSpace;
        Assert.Contains($"Oui{nnbsp}! À 10:30{nnbsp}?\n", Render(d, "fr"));
    }

    [Fact]
    public void Line_breaks_become_hard_breaks_and_stay_inside_their_list_item()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Highlights");
        d.Add(section, "bullet", en: "First line\nsecond line\n\n- not a new item");
        d.Add(section, "paragraph", en: "One\nTwo");

        var md = Render(d, "en");

        Assert.Contains("- First line\\\n  second line\\\n  \\- not a new item\n\nOne\\\nTwo\n", md);
    }

    [Fact]
    public void Lines_without_text_in_the_locale_are_left_out()
    {
        var (sample, ids) = TestDraft.Sample();
        sample.Omit(ids.Bullet, "fr");
        var section = sample.Add(sample.Root, "section", en: "Education"); // no fr text: missing
        sample.Add(section, "bullet", en: "Only in English");

        var md = Render(sample, "fr");

        Assert.DoesNotContain("machine à différences", md);
        Assert.DoesNotContain("Education", md);
        Assert.DoesNotContain("- \n", md);
        Assert.DoesNotContain("\n\n\n", md);
    }

    [Fact]
    public void An_empty_document_renders_as_nothing() => Assert.Equal("", Render(new TestDraft(), "en"));

    [Fact]
    public void Stored_text_cannot_become_markup()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "## Not a subheading");
        d.Add(section, "paragraph", en: "[click](javascript:alert(1)) ![img](x.png) <script>alert(1)</script> *bold* _em_ `code` ~~gone~~ a|b \\ &amp;");
        d.Add(section, "paragraph", en: "1. not a list");
        d.Add(section, "paragraph", en: "> not a quote");
        d.Add(section, "paragraph", en: "+ not a list either");
        d.Add(section, "paragraph", en: "===");

        var md = Render(d, "en");

        Assert.Contains("## \\## Not a subheading\n", md); // only a leading # starts a heading
        Assert.Contains(
            "\\[click\\](javascript:alert(1)) !\\[img\\](x.png) \\<script\\>alert(1)\\</script\\> \\*bold\\* \\_em\\_ \\`code\\` \\~\\~gone\\~\\~ a\\|b \\\\ \\&amp;\n",
            md);
        Assert.Contains("\n1\\. not a list\n", md);
        Assert.Contains("\n\\> not a quote\n", md);
        Assert.Contains("\n\\+ not a list either\n", md);
        Assert.Contains("\n\\===\n", md);
    }

    [Theory]
    [InlineData("R&D", "R&D")]
    [InlineData("AT&T; Bell Labs", "AT\\&T; Bell Labs")]
    [InlineData("&#169; 2026", "\\&#169; 2026")]
    [InlineData("- dash", "\\- dash")]
    [InlineData("2021. A year", "2021\\. A year")]
    [InlineData("Version 2.0", "Version 2.0")]
    public void Escaping_keeps_ordinary_text_readable(string text, string expected) =>
        Assert.Equal(expected, MarkdownRenderer.Escape(text));
}
