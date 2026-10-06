using Cv.Core.Drafts;
using Cv.Core.Model;

namespace Cv.Testing;

/// <summary>
/// Test data shared by the .NET test projects (linked into each). <see cref="Catalog"/> mirrors
/// the seed data in the InitialCreate migration; the integration tests use the real database's
/// catalog, so a drift between the two would show up there.
/// </summary>
public static class TestDocuments
{
    public static CvCatalog Catalog { get; } = new(
        [
            new LocaleInfo("en", "English", IsSource: true, IsPublishable: true),
            new LocaleInfo("nb", "Norsk bokmål", IsSource: false, IsPublishable: true),
            new LocaleInfo("fr", "Français", IsSource: false, IsPublishable: true),
            new LocaleInfo("zxx", "Language-neutral", IsSource: false, IsPublishable: false),
        ],
        [
            new NodeTypeInfo("root", "The document itself.", HasText: false),
            new NodeTypeInfo("name", "Your name.", true),
            new NodeTypeInfo("headline", "One-line title.", true),
            new NodeTypeInfo("contact", "Email, phone, URL or location.", true),
            new NodeTypeInfo("section", "Top-level heading.", true),
            new NodeTypeInfo("entry", "A role, degree or project.", true),
            new NodeTypeInfo("subtitle", "The organisation of an entry.", true),
            new NodeTypeInfo("location", "Where an entry took place.", true),
            new NodeTypeInfo("paragraph", "Free text.", true),
            new NodeTypeInfo("bullet", "One bullet point.", true),
            new NodeTypeInfo("skill_group", "A labelled group of skills.", true),
            new NodeTypeInfo("skill", "A single skill.", true),
        ],
        [
            ("root", "name"), ("root", "headline"), ("root", "contact"), ("root", "section"),
            ("section", "entry"), ("section", "paragraph"), ("section", "bullet"), ("section", "skill_group"), ("section", "skill"),
            ("entry", "subtitle"), ("entry", "location"), ("entry", "paragraph"), ("entry", "bullet"),
            ("skill_group", "skill"),
        ]);
}

/// <summary>
/// Builds a <see cref="Draft"/> line by line, the way the editor sends one:
/// <code>
/// var d = new TestDraft();
/// var section = d.Add(d.Root, "section", en: "Experience", nb: "Erfaring");
/// </code>
/// </summary>
public sealed class TestDraft
{
    private readonly List<DraftNode> _nodes = [];
    private readonly List<DraftText> _texts = [];

    public TestDraft()
    {
        Root = Guid.CreateVersion7();
        _nodes.Add(new DraftNode(Root, null, CvCatalog.RootType));
    }

    public Guid Root { get; }

    /// <summary>Adds a line as the last child of <paramref name="parent"/>, with text in any of the locales.</summary>
    public Guid Add(
        Guid parent,
        string type,
        string? en = null,
        string? nb = null,
        string? fr = null,
        string? zxx = null,
        IReadOnlyDictionary<string, string>? attrs = null)
    {
        var id = Guid.CreateVersion7();
        // Keep document order: insert after the parent's last descendant.
        var index = LastDescendantIndex(parent) + 1;
        _nodes.Insert(index, new DraftNode(id, parent, type, attrs));
        foreach (var (locale, content) in new[] { ("en", en), ("nb", nb), ("fr", fr), ("zxx", zxx) })
        {
            if (content is not null)
            {
                _texts.Add(new DraftText(id, locale, content));
            }
        }
        return id;
    }

    public TestDraft Omit(Guid node, string locale)
    {
        _texts.RemoveAll(t => t.NodeId == node && t.Locale == locale);
        _texts.Add(new DraftText(node, locale, null, Omitted: true));
        return this;
    }

    public Draft ToDraft() => new(_nodes.ToList(), _texts.ToList());

    private int LastDescendantIndex(Guid node)
    {
        var descendants = new HashSet<Guid> { node };
        var last = _nodes.FindIndex(n => n.Id == node);
        for (var i = last + 1; i < _nodes.Count && _nodes[i].ParentId is { } parent && descendants.Contains(parent); i++)
        {
            descendants.Add(_nodes[i].Id);
            last = i;
        }
        return last;
    }

    /// <summary>A realistic three-language CV used across tests. Everything is translated, so every locale can be published.</summary>
    public static (TestDraft Draft, SampleIds Ids) Sample()
    {
        var d = new TestDraft();
        var name = d.Add(d.Root, "name", zxx: "Ada Lovelace");
        var headline = d.Add(d.Root, "headline", en: "Software engineer", nb: "Programvareutvikler", fr: "Ingénieure logiciel");
        var email = d.Add(d.Root, "contact", zxx: "ada@example.com", attrs: new Dictionary<string, string> { ["kind"] = "email" });
        var experience = d.Add(d.Root, "section", en: "Experience", nb: "Erfaring", fr: "Expérience");
        var job = d.Add(experience, "entry", en: "Lead engineer", nb: "Ledende utvikler", fr: "Ingénieure principale",
            attrs: new Dictionary<string, string> { ["start"] = "2021-03" });
        var company = d.Add(job, "subtitle", zxx: "Analytical Engines Ltd, London");
        var bullet = d.Add(job, "bullet", en: "Built the difference engine", nb: "Bygde differansemaskinen", fr: "A construit la machine à différences");
        return (d, new SampleIds(name, headline, email, experience, job, company, bullet));
    }
}

public sealed record SampleIds(Guid Name, Guid Headline, Guid Email, Guid Experience, Guid Job, Guid Company, Guid Bullet);
