using Cv.Core.Drafts;
using Cv.Core.Localization;
using Cv.Testing;

namespace Cv.Core.Tests.Drafts;

public class DraftOutlineTests
{
    private static readonly Model.CvCatalog Catalog = TestDocuments.Catalog;

    private const string Outline = """
        // A comment, as a hand-written seed file would have.
        {
          "children": [
            { "type": "name", "zxx": "Ada Lovelace" },
            { "type": "section", "en": "Experience", "nb": "Erfaring", "children": [
              { "type": "entry", "en": "Engineer", "attrs": { "start": "2021-03" }, "omit": ["fr"], "children": [
                { "type": "subtitle", "zxx": "Analytical Engines Ltd" },
                { "type": "location", "en": "London, UK" },
                { "type": "bullet", "en": "Built the difference engine." },
              ] },
            ] },
          ],
        }
        """;

    [Fact]
    public void An_outline_becomes_a_valid_draft_in_document_order()
    {
        var draft = DraftOutline.Parse(Outline, Catalog);
        var result = DraftBuilder.Build(draft, null, Catalog);

        Assert.Empty(result.Issues);
        Assert.Equal(["root", "name", "section", "entry", "subtitle", "location", "bullet"], draft.Nodes.Select(n => n.Type));
        Assert.Equal(draft.Nodes[3].Id, draft.Nodes[6].ParentId);
        Assert.Equal(new Dictionary<string, string> { ["start"] = "2021-03" }, draft.Nodes[3].Attrs);

        var english = Localizer.Localize(result.Document, Catalog, "en");
        Assert.Equal("Built the difference engine.", english.Nodes.Last().Content);
        var french = Localizer.Localize(result.Document, Catalog, "fr");
        Assert.True(french.Nodes.Single(n => n.Node.Type == "bullet").IsHidden); // the entry is omitted in fr
    }

    [Theory]
    [InlineData("""{ "children": [ { "type": "name", "zxx": "Ada", "tittle": "x" } ] }""", "$.children[0].tittle: unknown property")]
    [InlineData("""{ "children": [ { "zxx": "Ada" } ] }""", "$.children[0]: every line needs a \"type\"")]
    [InlineData("""{ "children": [ { "type": "name", "en": 42 } ] }""", "$.children[0].en: must be a string, not number")]
    [InlineData("""{ "children": [ { "type": "bullet", "omit": ["xx"] } ] }""", "$.children[0].omit: unknown locale \"xx\"")]
    [InlineData("""{ "type": "section" }""", "$: the top level is the document itself")]
    [InlineData("""{ "children": [ """, "$: not valid JSON")]
    public void Mistakes_are_reported_with_the_path_of_the_line(string json, string expectedStart)
    {
        var error = Assert.Throws<OutlineFormatException>(() => DraftOutline.Parse(json, Catalog));

        Assert.StartsWith(expectedStart, error.Message);
    }

    [Fact]
    public void Grammar_errors_are_left_to_the_draft_builder_which_names_the_line()
    {
        var draft = DraftOutline.Parse("""{ "children": [ { "type": "bullet", "en": "Directly under the root" } ] }""", Catalog);

        var issue = Assert.Single(DraftBuilder.Build(draft, null, Catalog).Issues);

        Assert.Equal("grammar", issue.Code);
        Assert.Equal(draft.Nodes[1].Id, issue.NodeId);
    }
}
