using Cv.Core.Drafts;
using Cv.Core.Hashing;
using Cv.Core.Model;
using Cv.Core.Validation;
using Cv.Testing;

namespace Cv.Core.Tests.Validation;

public class DocumentValidatorTests
{
    private static readonly CvCatalog Catalog = TestDocuments.Catalog;

    private static CvDocument Valid() => DraftBuilder.Build(TestDraft.Sample().Draft.ToDraft(), null, Catalog).Document;

    private static IReadOnlyList<ValidationIssue> Validate(CvDocument document) => DocumentValidator.Validate(document, Catalog);

    private static CvDocument With(CvDocument document, IEnumerable<DocumentNode>? nodes = null, IEnumerable<DocumentText>? texts = null) =>
        new(nodes ?? document.Nodes, texts ?? document.Texts);

    [Fact]
    public void A_complete_document_has_no_issues() => Assert.Empty(Validate(Valid()));

    [Fact]
    public void The_grammar_is_enforced()
    {
        var document = Valid();
        var bulletUnderRoot = new DocumentNode(Guid.CreateVersion7(), document.Root!.Id, "bullet", "b00", DocumentNode.NoAttrs);

        var issues = Validate(With(document, nodes: [.. document.Nodes, bulletUnderRoot]));

        var issue = Assert.Single(issues);
        Assert.Equal(DocumentValidator.Codes.Grammar, issue.Code);
        Assert.Equal(bulletUnderRoot.Id, issue.NodeId);
    }

    [Fact]
    public void Exactly_one_root_is_required()
    {
        var document = Valid();
        var secondRoot = new DocumentNode(Guid.CreateVersion7(), null, "root", "a0", DocumentNode.NoAttrs);

        Assert.Contains(Validate(With(document, nodes: [.. document.Nodes, secondRoot])), i => i.Code == DocumentValidator.Codes.Root);
        Assert.Contains(Validate(CvDocument.Empty), i => i.Code == DocumentValidator.Codes.Root);
    }

    [Fact]
    public void A_cycle_is_reported_as_unreachable()
    {
        var document = Valid();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        DocumentNode[] cycle =
        [
            new(a, b, "entry", "a0", DocumentNode.NoAttrs),
            new(b, a, "entry", "a0", DocumentNode.NoAttrs),
        ];

        var issues = Validate(With(document, nodes: [.. document.Nodes, .. cycle]));

        Assert.Contains(issues, i => i.Code == DocumentValidator.Codes.Unreachable && i.NodeId == a);
    }

    [Fact]
    public void Siblings_cannot_share_a_sort_key()
    {
        var document = Valid();
        var experience = document.Nodes.Single(n => n.Type == "section");
        var clash = new DocumentNode(Guid.CreateVersion7(), document.Root!.Id, "section", experience.SortKey, DocumentNode.NoAttrs);

        Assert.Contains(Validate(With(document, nodes: [.. document.Nodes, clash])), i => i.Code == DocumentValidator.Codes.SortKey);
    }

    [Theory]
    [InlineData("zxx", null, true, "Language-neutral text cannot be omitted.")]
    [InlineData("en", "   ", false, "Text cannot be blank.")]
    [InlineData("xx", "Hello", false, "Unknown locale 'xx'.")]
    public void Text_rules_are_enforced(string locale, string? content, bool omitted, string message)
    {
        var document = Valid();
        var bullet = document.Nodes.Single(n => n.Type == "bullet");
        var texts = document.Texts.Where(t => !(t.NodeId == bullet.Id && t.Locale == locale))
            .Append(new DocumentText(bullet.Id, locale, content, omitted, null));

        Assert.Contains(Validate(With(document, texts: texts)), i => i.Message == message && i.NodeId == bullet.Id);
    }

    [Fact]
    public void The_root_carries_no_text()
    {
        var document = Valid();

        var issues = Validate(With(document, texts: [.. document.Texts, new DocumentText(document.Root!.Id, "en", "Hello", false, null)]));

        Assert.Contains(issues, i => i.NodeId == document.Root!.Id && i.Message.Contains("carries no text"));
    }

    [Fact]
    public void Only_translations_have_a_source_hash()
    {
        var document = Valid();
        var bullet = document.Nodes.Single(n => n.Type == "bullet");
        var texts = document.Texts.Select(t => t.NodeId == bullet.Id && t.Locale == "en" ? t with { SourceHash = Sha256Digest.OfText("x") } : t);

        Assert.Contains(Validate(With(document, texts: texts)), i => i.NodeId == bullet.Id && i.Locale == "en");
    }

    [Theory]
    [InlineData("entry", "start", "2021-13", "'start' must be a year")]
    [InlineData("entry", "colour", "red", "Unknown attribute 'colour'")]
    [InlineData("contact", "kind", "fax", "'kind' must be one of")]
    [InlineData("bullet", "start", "2021", "A bullet has no attributes")]
    public void Attributes_are_checked(string type, string key, string value, string message)
    {
        var problems = NodeAttributes.Validate(type, new Dictionary<string, string> { [key] = value });

        Assert.Contains(problems, p => p.StartsWith(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2021-03", "2020-12", true)]
    [InlineData("2021-03", "2021-02", true)]
    [InlineData("2021-03", "2021", false)] // same year at the coarser precision
    [InlineData("2021", "2021-01", false)]
    [InlineData("2021-03", "2021-03", false)]
    public void An_end_date_before_the_start_is_rejected(string start, string end, bool rejected)
    {
        var problems = NodeAttributes.Validate("entry", new Dictionary<string, string> { ["start"] = start, ["end"] = end });

        Assert.Equal(rejected, problems.Any(p => p.Contains("before the start", StringComparison.Ordinal)));
    }
}
