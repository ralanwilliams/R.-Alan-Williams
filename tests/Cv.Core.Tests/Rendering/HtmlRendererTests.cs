using Cv.Core.Drafts;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Core.Rendering;
using Cv.Testing;

namespace Cv.Core.Tests.Rendering;

public class HtmlRendererTests
{
    private static readonly CvCatalog Catalog = TestDocuments.Catalog;

    private static string Render(TestDraft draft, string locale, RenderOptions? options = null) =>
        HtmlRenderer.Render(Localizer.Localize(DraftBuilder.Build(draft.ToDraft(), null, Catalog).Document, Catalog, locale), options);

    [Fact]
    public void The_document_declares_its_language_and_title()
    {
        var html = Render(TestDraft.Sample().Draft, "nb");

        Assert.Contains("<html lang=\"nb\">", html);
        Assert.Contains("<title>Ada Lovelace – CV</title>", html);
        Assert.Contains("<h1 class=\"cv-name\">Ada Lovelace</h1>", html);
        Assert.Contains("Erfaring", html);
    }

    [Fact]
    public void Text_is_HTML_encoded()
    {
        var d = new TestDraft();
        d.Add(d.Add(d.Root, "section", en: "Skills"), "bullet", en: "<script>alert('x')</script> & \"more\"");

        var html = Render(d, "en");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;more&quot;", html);
    }

    [Theory]
    [InlineData("email", "ada@example.com", "mailto:ada@example.com")]
    [InlineData("phone", "+47 912 34 567", "tel:+4791234567")]
    [InlineData("url", "example.com/ada", "https://example.com/ada")]
    [InlineData("url", "https://example.com", "https://example.com/")]
    [InlineData("url", "javascript://%0aalert(1)", null)]
    [InlineData("url", "javascript:alert(1)", null)]
    [InlineData("location", "Oslo", null)]
    public void Contact_links_use_only_safe_schemes(string kind, string text, string? expectedHref)
    {
        var d = new TestDraft();
        d.Add(d.Root, "contact", zxx: text, attrs: new Dictionary<string, string> { ["kind"] = kind });

        var html = Render(d, "en");

        if (expectedHref is null)
        {
            Assert.DoesNotContain("<a ", html);
        }
        else
        {
            Assert.Contains($"<a href=\"{expectedHref}\">", html);
        }
    }

    [Theory]
    [InlineData("en", "Mar 2021 – Present")]
    [InlineData("nb", "mars 2021 – nå")]
    [InlineData("fr", "mars 2021 – aujourd’hui")]
    public void Dates_are_formatted_per_locale(string locale, string expected)
    {
        var html = Render(TestDraft.Sample().Draft, locale);

        Assert.Contains($"<span class=\"cv-dates\">{expected}</span>", html);
    }

    [Fact]
    public void An_entry_shows_organisation_and_location_above_title_and_dates()
    {
        var d = new TestDraft();
        var job = d.Add(d.Add(d.Root, "section", en: "Experience"), "entry", en: "Frontend Lead",
            attrs: new Dictionary<string, string> { ["start"] = "2026-05" });
        d.Add(job, "bullet", en: "Led the team"); // order in the tree doesn't matter for the header
        d.Add(job, "location", en: "Haugesund, Norway");
        d.Add(job, "subtitle", zxx: "Norwegian Maritime Authority");

        var html = Render(d, "en");

        Assert.Contains(
            "<p class=\"cv-entry-where\"><span class=\"cv-subtitle\">Norwegian Maritime Authority</span><span class=\"cv-sep\"> | </span><span class=\"cv-location\">Haugesund, Norway</span></p>\n" +
            "<h3 class=\"cv-entry-what\"><span class=\"cv-entry-title\">Frontend Lead</span><span class=\"cv-sep\"> | </span><span class=\"cv-dates\">May 2026 – Present</span></h3>\n" +
            "<ul class=\"cv-bullets\">\n<li>Led the team</li>",
            html);
    }

    [Fact]
    public void An_entry_without_organisation_or_title_still_shows_what_it_has()
    {
        var d = new TestDraft();
        d.Add(d.Add(d.Root, "section", en: "Education"), "entry", attrs: new Dictionary<string, string> { ["end"] = "2020" });

        var html = Render(d, "en");

        Assert.DoesNotContain("<p class=\"cv-entry-where\">", html);
        Assert.Contains("<h3 class=\"cv-entry-what\"><span class=\"cv-dates\">2020</span></h3>", html);
    }

    [Theory]
    [InlineData("en", "Languages:")]
    [InlineData("fr", "Langues :")] // French puts a (narrow, unbreakable) space before the colon
    public void A_skill_group_is_one_labelled_comma_separated_line(string locale, string label)
    {
        var d = new TestDraft();
        var group = d.Add(d.Add(d.Root, "section", en: "Skills", fr: "Compétences"), "skill_group", en: "Languages", fr: "Langues");
        d.Add(group, "skill", zxx: "C#");
        d.Add(group, "skill", zxx: "TypeScript");

        var html = Render(d, locale);

        Assert.Contains(
            $"<p class=\"cv-skill-group\"><span class=\"cv-skill-group-label\">{label}</span> <span class=\"cv-skill\">C#</span>, <span class=\"cv-skill\">TypeScript</span></p>",
            html);
    }

    [Fact]
    public void French_high_punctuation_gets_a_narrow_no_break_space()
    {
        var d = new TestDraft();
        d.Add(d.Add(d.Root, "section", en: "Note", nb: "Notat", fr: "Note"), "paragraph",
            en: "Hi!", nb: "Hei!", fr: "Langues : français, anglais ; « oui » ! Rendez-vous à 10:30 ?");

        var html = Render(d, "fr");

        const char nnbsp = Typography.NarrowNoBreakSpace;
        Assert.Contains($"Langues{nnbsp}: français, anglais{nnbsp}; «{nnbsp}oui{nnbsp}»{nnbsp}! Rendez-vous à 10:30{nnbsp}?", html);
    }

    [Fact]
    public void Omitted_lines_are_left_out_and_missing_ones_shown_only_in_the_preview()
    {
        var (sample, ids) = TestDraft.Sample();
        sample.Omit(ids.Bullet, "fr");
        var section = sample.Add(sample.Root, "section", en: "Education"); // no fr text: missing

        var download = Render(sample, "fr");
        var preview = Render(sample, "fr", new RenderOptions(Preview: true));

        Assert.DoesNotContain("machine à différences", download);
        Assert.DoesNotContain("<span class=\"cv-missing\">", download); // the inlined CSS mentions the class; the element must not appear
        Assert.Contains("<span class=\"cv-missing\">Texte manquant</span>", preview);
        Assert.Contains($"data-node-id=\"{section}\"", preview);
        Assert.DoesNotContain("data-node-id=", download);
    }

    [Fact]
    public void A_download_leaves_out_lines_with_missing_text_instead_of_printing_empty_ones()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Highlights", nb: "Høydepunkter");
        var entry = d.Add(section, "entry", en: "Engineer");  // no nb title
        d.Add(entry, "bullet", en: "Only in English");       // no nb text
        d.Add(entry, "bullet", en: "Both", nb: "Begge");

        var html = Render(d, "nb");

        Assert.DoesNotContain("<li></li>", html);
        Assert.DoesNotContain("<h3 class=\"cv-entry-title\"></h3>", html);
        Assert.Contains("<ul class=\"cv-bullets\">\n<li>Begge</li>\n</ul>", html);
    }

    [Fact]
    public void Consecutive_bullets_share_one_list()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Highlights");
        d.Add(section, "bullet", en: "One");
        d.Add(section, "bullet", en: "Two");

        var html = Render(d, "en");

        Assert.Equal(1, html.Split("<ul class=\"cv-bullets\">").Length - 1);
        Assert.Contains("<li>One</li>\n<li>Two</li>", html);
    }

    [Fact]
    public void The_stylesheet_can_be_linked_instead_of_inlined()
    {
        var linked = Render(TestDraft.Sample().Draft, "en", new RenderOptions(StylesheetHref: "/render/cv.css"));
        var inlined = Render(TestDraft.Sample().Draft, "en");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/render/cv.css\">", linked);
        Assert.DoesNotContain("<style>", linked);
        Assert.Contains("<style>", inlined);
        Assert.Contains("@page", HtmlRenderer.Css);
    }
}

public class MessagesTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("nb")]
    [InlineData("fr")]
    public void Every_publishable_locale_has_complete_messages(string locale)
    {
        var messages = Messages.For(locale);

        Assert.Equal(12, messages.Months.Count);
        Assert.Contains("{start}", messages.DateRange);
        Assert.Contains("{end}", messages.DateRange);
        Assert.Contains("{name}", messages.DocumentTitle);
        Assert.Contains("{label}", messages.Label);
    }

    [Fact]
    public void Unknown_locales_have_no_messages() => Assert.Null(Messages.Find("xx"));

    [Theory]
    [InlineData("2021-03", null, "Mar 2021 – Present")]
    [InlineData("2019", "2021", "2019 – 2021")]
    [InlineData(null, "2021-12", "Dec 2021")]
    [InlineData(null, null, null)]
    public void Date_ranges(string? start, string? end, string? expected) =>
        Assert.Equal(expected, Messages.For("en").FormatRange(start, end));
}
