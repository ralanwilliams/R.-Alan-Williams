namespace Cv.Core.Model;

/// <summary>A BCP 47 locale as stored in <c>cv.locales</c>.</summary>
public sealed record LocaleInfo(string Code, string Name, bool IsSource, bool IsPublishable);

/// <summary>A kind of node as stored in <c>cv.node_types</c>.</summary>
public sealed record NodeTypeInfo(string Code, string Description, bool HasText);

/// <summary>
/// The reference data every document is checked against: the locales and the document
/// grammar (which node type may contain which). It is seed data in the database and only
/// changes through a migration, so callers load it once and share it.
/// </summary>
public sealed class CvCatalog
{
    /// <summary>BCP 47 "no linguistic content": text shared by every locale (names, URLs...).</summary>
    public const string NeutralLocale = "zxx";

    public const string RootType = "root";

    /// <summary>
    /// The order node types are offered in. The database has no ordering column, and the
    /// editor should not list "skill" before "section" just because of the alphabet.
    /// Types not listed here (added by a later migration) sort after these, alphabetically.
    /// </summary>
    private static readonly string[] PreferredTypeOrder =
        ["root", "name", "headline", "contact", "section", "entry", "subtitle", "location", "paragraph", "bullet", "skill_group", "skill"];

    private readonly Dictionary<string, LocaleInfo> _locales;
    private readonly Dictionary<string, NodeTypeInfo> _types;
    private readonly Dictionary<string, IReadOnlyList<string>> _children;

    public CvCatalog(
        IEnumerable<LocaleInfo> locales,
        IEnumerable<NodeTypeInfo> nodeTypes,
        IEnumerable<(string Parent, string Child)> grammar)
    {
        _locales = locales.ToDictionary(l => l.Code, StringComparer.Ordinal);
        _types = nodeTypes.ToDictionary(t => t.Code, StringComparer.Ordinal);

        Source = _locales.Values.SingleOrDefault(l => l.IsSource)
            ?? throw new ArgumentException("Exactly one locale must be the source locale.", nameof(locales));

        // Source first, then the other publishable locales alphabetically by code.
        Locales = _locales.Values
            .OrderByDescending(l => l.IsSource)
            .ThenByDescending(l => l.IsPublishable)
            .ThenBy(l => l.Code, StringComparer.Ordinal)
            .ToArray();
        PublishableLocales = Locales.Where(l => l.IsPublishable).ToArray();

        NodeTypes = _types.Values.OrderBy(t => TypeRank(t.Code)).ThenBy(t => t.Code, StringComparer.Ordinal).ToArray();

        var rank = NodeTypes.Select((t, i) => (t.Code, i)).ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        _children = grammar
            .GroupBy(g => g.Parent, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.Child).Distinct(StringComparer.Ordinal)
                    .OrderBy(c => rank.GetValueOrDefault(c, int.MaxValue)).ToArray(),
                StringComparer.Ordinal);
    }

    public LocaleInfo Source { get; }

    /// <summary>All locales: source first, then other publishable ones, then <c>zxx</c>.</summary>
    public IReadOnlyList<LocaleInfo> Locales { get; }

    public IReadOnlyList<LocaleInfo> PublishableLocales { get; }

    public IReadOnlyList<NodeTypeInfo> NodeTypes { get; }

    public LocaleInfo? FindLocale(string code) => _locales.GetValueOrDefault(code);

    public NodeTypeInfo? FindType(string code) => _types.GetValueOrDefault(code);

    public bool CanContain(string parentType, string childType) =>
        _children.TryGetValue(parentType, out var children) && children.Contains(childType, StringComparer.Ordinal);

    public IReadOnlyList<string> AllowedChildren(string parentType) =>
        _children.GetValueOrDefault(parentType) ?? [];

    private static int TypeRank(string code)
    {
        var index = Array.IndexOf(PreferredTypeOrder, code);
        return index < 0 ? PreferredTypeOrder.Length : index;
    }
}
