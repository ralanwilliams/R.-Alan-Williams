using Cv.Core.Drafts;
using Cv.Core.Hashing;
using Cv.Core.Localization;
using Cv.Core.Validation;
using Cv.Testing;

namespace Cv.Core.Tests.Drafts;

public class DraftBuilderTests
{
    private static readonly Model.CvCatalog Catalog = TestDocuments.Catalog;

    private static Model.CvDocument Build(Draft draft, Model.CvDocument? baseDocument = null)
    {
        var result = DraftBuilder.Build(draft, baseDocument, Catalog);
        Assert.Empty(result.Issues);
        return result.Document;
    }

    /// <summary>Simulates the editor: load a saved version, change it, send it back.</summary>
    private static Draft Edit(Model.CvDocument saved, Func<DraftText, DraftText>? text = null, Func<IReadOnlyList<DraftNode>, IReadOnlyList<DraftNode>>? nodes = null)
    {
        var draft = Draft.FromDocument(saved);
        return new Draft(nodes?.Invoke(draft.Nodes) ?? draft.Nodes, draft.Texts.Select(text ?? (t => t)).ToList());
    }

    [Fact]
    public void Loading_and_saving_unchanged_gives_the_same_content_hash()
    {
        var v1 = Build(TestDraft.Sample().Draft.ToDraft());

        var again = Build(Draft.FromDocument(v1), v1);

        // This is what lets "no changes" be detected, by the app and by the database trigger.
        Assert.Equal(ContentHash.Compute(v1), ContentHash.Compute(again));
    }

    [Fact]
    public void Text_is_trimmed_newline_normalised_and_NFC()
    {
        var d = new TestDraft();
        var paragraph = d.Add(d.Add(d.Root, "section", en: "About"), "paragraph", en: "  Café\r\nline two  ");

        var document = Build(d.ToDraft());

        Assert.Equal("Café\nline two", document.TextOf(paragraph, "en")!.Content);
    }

    [Fact]
    public void Empty_text_means_no_row_so_the_line_is_missing()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "   ");

        var document = Build(d.ToDraft());

        Assert.Null(document.TextOf(section, "en"));
        Assert.True(Localizer.Localize(document, Catalog, "en").Nodes.Single(n => n.Node.Id == section).IsMissing);
    }

    [Fact]
    public void A_new_translation_records_the_hash_of_the_current_source_text()
    {
        var (sample, ids) = TestDraft.Sample();

        var document = Build(sample.ToDraft());

        Assert.Equal(Sha256Digest.OfText("Experience"), document.TextOf(ids.Experience, "nb")!.SourceHash);
        Assert.Null(document.TextOf(ids.Experience, "en")!.SourceHash); // the source itself has none
        Assert.Null(document.TextOf(ids.Name, "zxx")!.SourceHash);      // nor does language-neutral text
    }

    [Fact]
    public void Changing_the_source_makes_unchanged_translations_stale()
    {
        var (sample, ids) = TestDraft.Sample();
        var v1 = Build(sample.ToDraft());

        var v2 = Build(Edit(v1, t => t.NodeId == ids.Experience && t.Locale == "en" ? t with { Content = "Work experience" } : t), v1);

        Assert.Equal(Sha256Digest.OfText("Experience"), v2.TextOf(ids.Experience, "nb")!.SourceHash); // kept from v1
        Assert.True(Localizer.Localize(v2, Catalog, "nb").Nodes.Single(n => n.Node.Id == ids.Experience).IsStale);
        Assert.False(Localizer.Localize(v2, Catalog, "en").Nodes.Single(n => n.Node.Id == ids.Experience).IsStale);
    }

    [Fact]
    public void Editing_or_reviewing_a_translation_brings_it_up_to_date()
    {
        var (sample, ids) = TestDraft.Sample();
        var v1 = Build(sample.ToDraft());
        var v2 = Build(Edit(v1, t => t.NodeId == ids.Experience && t.Locale == "en" ? t with { Content = "Work experience" } : t), v1);

        var v3 = Build(Edit(v2, t => (t.NodeId, t.Locale) switch
        {
            var (n, l) when n == ids.Experience && l == "nb" => t with { Content = "Arbeidserfaring" }, // edited
            var (n, l) when n == ids.Experience && l == "fr" => t with { Reviewed = true },             // checked, unchanged
            _ => t,
        }), v2);

        var expected = Sha256Digest.OfText("Work experience");
        Assert.Equal(expected, v3.TextOf(ids.Experience, "nb")!.SourceHash);
        Assert.Equal(expected, v3.TextOf(ids.Experience, "fr")!.SourceHash);
        Assert.Equal(0, Localizer.Localize(v3, Catalog, "fr").Readiness.StaleCount);
    }

    [Fact]
    public void Inserting_a_line_keeps_the_sort_keys_of_its_siblings()
    {
        var (sample, ids) = TestDraft.Sample();
        var v1 = Build(sample.ToDraft());
        var newBullet = Guid.CreateVersion7();

        // Insert a bullet before the existing one, i.e. directly after the job's subtitle.
        var v2 = Build(Edit(v1, nodes: list =>
        {
            var copy = list.ToList();
            copy.Insert(copy.FindIndex(n => n.Id == ids.Bullet), new DraftNode(newBullet, ids.Job, "bullet"));
            return copy;
        }), v1);

        Assert.Equal(v1.Find(ids.Bullet)!.SortKey, v2.Find(ids.Bullet)!.SortKey);
        Assert.Equal(v1.Find(ids.Company)!.SortKey, v2.Find(ids.Company)!.SortKey);
        Assert.Equal([ids.Company, newBullet, ids.Bullet], v2.ChildrenOf(ids.Job).Select(n => n.Id));
    }

    [Fact]
    public void Duplicate_node_ids_are_reported()
    {
        var draft = TestDraft.Sample().Draft.ToDraft();
        var duplicated = draft with { Nodes = [.. draft.Nodes, draft.Nodes[^1]] };

        var result = DraftBuilder.Build(duplicated, null, Catalog);

        Assert.Contains(result.Issues, i => i.Code == DocumentValidator.Codes.DuplicateNode);
    }

    [Fact]
    public void Attribute_values_are_trimmed_and_empty_ones_dropped()
    {
        var d = new TestDraft();
        var job = d.Add(d.Add(d.Root, "section", en: "Experience"), "entry", en: "Engineer",
            attrs: new Dictionary<string, string> { ["start"] = " 2021-03 ", ["end"] = "" });

        var document = Build(d.ToDraft());

        Assert.Equal(new Dictionary<string, string> { ["start"] = "2021-03" }, document.Find(job)!.Attrs);
    }

    [Fact]
    public void The_starter_document_is_valid()
    {
        var draft = StarterDraft.Create("Ada Lovelace", "ada@example.com", Catalog);

        var result = DraftBuilder.Build(draft, null, Catalog);

        Assert.Empty(result.Issues);
        var english = Localizer.Localize(result.Document, Catalog, "en");
        Assert.Equal("Ada Lovelace", english.Nodes.Single(n => n.Node.Type == "name").Content);
        Assert.True(english.Readiness.MissingCount > 0); // the empty lines are there to be written
    }
}
