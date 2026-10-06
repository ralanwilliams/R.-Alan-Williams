using Cv.Core.Drafts;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Testing;

namespace Cv.Core.Tests.Localization;

public class LocalizerTests
{
    private static readonly CvCatalog Catalog = TestDocuments.Catalog;

    private static CvDocument Build(TestDraft draft) => DraftBuilder.Build(draft.ToDraft(), null, Catalog).Document;

    private static LocalizedNode Node(CvDocument document, string locale, Guid id) =>
        Localizer.Localize(document, Catalog, locale).Nodes.Single(n => n.Node.Id == id);

    [Fact]
    public void Language_neutral_text_appears_in_every_locale()
    {
        var (sample, ids) = TestDraft.Sample();
        var document = Build(sample);

        Assert.All(Catalog.PublishableLocales, locale =>
        {
            var name = Node(document, locale.Code, ids.Name);
            Assert.Equal("Ada Lovelace", name.Content);
            Assert.False(name.IsMissing);
        });
    }

    [Fact]
    public void A_locale_can_override_language_neutral_text()
    {
        var d = new TestDraft();
        var city = d.Add(d.Root, "contact", zxx: "Oslo", fr: "Oslo (Norvège)");
        var document = Build(d);

        Assert.Equal("Oslo (Norvège)", Node(document, "fr", city).Content);
        Assert.Equal("Oslo", Node(document, "nb", city).Content);
    }

    [Fact]
    public void Omitting_a_line_hides_its_whole_subtree_without_making_it_missing()
    {
        var (sample, ids) = TestDraft.Sample();
        sample.Omit(ids.Job, "fr");
        var document = Build(sample);

        foreach (var id in new[] { ids.Job, ids.Company, ids.Bullet })
        {
            var node = Node(document, "fr", id);
            Assert.True(node.IsHidden);
            Assert.False(node.IsMissing);
            Assert.Null(node.Content);
        }
        Assert.False(Node(document, "nb", ids.Bullet).IsHidden);
        Assert.True(Localizer.Localize(document, Catalog, "fr").Readiness.CanPublish);
    }

    [Fact]
    public void A_line_without_text_is_missing_and_blocks_publishing()
    {
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Experience", nb: "Erfaring");
        var document = Build(d);

        Assert.True(Node(document, "fr", section).IsMissing);
        var readiness = Localizer.Localize(document, Catalog, "fr").Readiness;
        Assert.Equal(1, readiness.MissingCount);
        Assert.False(readiness.CanPublish);
        Assert.True(Localizer.Localize(document, Catalog, "nb").Readiness.CanPublish);
    }

    [Fact]
    public void The_root_is_never_missing_because_it_has_no_text()
    {
        var document = Build(new TestDraft());

        Assert.All(Localizer.LocalizeAll(document, Catalog), l => Assert.True(l.Readiness.CanPublish));
    }

    [Fact]
    public void A_translation_without_a_source_hash_counts_as_stale()
    {
        var (sample, ids) = TestDraft.Sample();
        var built = Build(sample);
        // As if imported: no record of which English text it came from (ADR 0001 §6).
        var document = new CvDocument(built.Nodes, built.Texts.Select(t => t.Locale == "nb" ? t with { SourceHash = null } : t));

        Assert.True(Node(document, "nb", ids.Experience).IsStale);
        Assert.False(Node(document, "nb", ids.Name).IsStale); // shared text is never a translation
    }

    [Fact]
    public void Nodes_come_in_document_order_by_ordinal_sort_key()
    {
        var root = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var document = new CvDocument(
            [
                new DocumentNode(root, null, "root", "a0", DocumentNode.NoAttrs),
                new DocumentNode(second, root, "contact", "a0", DocumentNode.NoAttrs),
                new DocumentNode(first, root, "contact", "Zz", DocumentNode.NoAttrs), // "Zz" < "a0" in the C collation
            ],
            []);

        var order = Localizer.Localize(document, Catalog, "en").Nodes.Select(n => n.Node.Id);

        Assert.Equal([root, first, second], order);
    }
}
