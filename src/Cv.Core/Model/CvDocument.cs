using System.Collections.Immutable;
using Cv.Core.Hashing;

namespace Cv.Core.Model;

/// <summary>
/// One node of the tree. <see cref="Id"/> is stable across versions (ADR 0001 §2);
/// <see cref="SortKey"/> orders siblings (§8); <see cref="Attrs"/> holds language-neutral data.
/// </summary>
public sealed record DocumentNode(
    Guid Id,
    Guid? ParentId,
    string Type,
    string SortKey,
    ImmutableSortedDictionary<string, string> Attrs)
{
    public static readonly ImmutableSortedDictionary<string, string> NoAttrs =
        ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// The text of one node in one locale. <see cref="Content"/> is null exactly when the node is
/// deliberately omitted from that locale. <see cref="SourceHash"/> records which source-locale
/// text a translation was made from, so a later change to the source makes it stale.
/// </summary>
public sealed record DocumentText(
    Guid NodeId,
    string Locale,
    string? Content,
    bool IsOmitted,
    Sha256Digest? SourceHash);

/// <summary>
/// A complete CV: the tree shared by every language plus the text per locale. This is the
/// in-memory form of one row in <c>cv_versions</c> with its <c>cv_nodes</c> and
/// <c>cv_node_contents</c>.
/// </summary>
/// <remarks>
/// Construction never throws on bad data, so an invalid document can still be inspected and
/// reported on. Use <see cref="Validation.DocumentValidator"/> to check it.
/// </remarks>
public sealed class CvDocument
{
    private static readonly IReadOnlyList<DocumentNode> NoChildren = [];

    private readonly Dictionary<Guid, DocumentNode> _nodes = [];
    private readonly Dictionary<Guid, List<DocumentNode>> _children = [];
    private readonly Dictionary<(Guid NodeId, string Locale), DocumentText> _texts = [];

    public CvDocument(IEnumerable<DocumentNode> nodes, IEnumerable<DocumentText> texts)
    {
        Nodes = nodes.ToArray();
        Texts = texts.ToArray();

        foreach (var node in Nodes)
        {
            if (!_nodes.TryAdd(node.Id, node))
            {
                continue;
            }

            if (node.ParentId is { } parentId)
            {
                if (!_children.TryGetValue(parentId, out var siblings))
                {
                    _children[parentId] = siblings = [];
                }
                siblings.Add(node);
            }
            else
            {
                Root ??= node;
            }
        }

        foreach (var siblings in _children.Values)
        {
            siblings.Sort((a, b) => string.CompareOrdinal(a.SortKey, b.SortKey));
        }

        foreach (var text in Texts)
        {
            _texts.TryAdd((text.NodeId, text.Locale), text);
        }
    }

    public static CvDocument Empty { get; } = new([], []);

    public IReadOnlyList<DocumentNode> Nodes { get; }

    public IReadOnlyList<DocumentText> Texts { get; }

    /// <summary>The first node without a parent, if any.</summary>
    public DocumentNode? Root { get; }

    public DocumentNode? Find(Guid nodeId) => _nodes.GetValueOrDefault(nodeId);

    /// <summary>Children in document order (sort key, ordinal comparison, like the "C" collation).</summary>
    public IReadOnlyList<DocumentNode> ChildrenOf(Guid nodeId) =>
        _children.TryGetValue(nodeId, out var children) ? children : NoChildren;

    public DocumentText? TextOf(Guid nodeId, string locale) => _texts.GetValueOrDefault((nodeId, locale));

    /// <summary>
    /// Every node reachable from the root, depth first in document order, with its depth
    /// (root = 0). Nodes that are not connected to the root are skipped, as in
    /// <c>cv.cv_version_localized</c>.
    /// </summary>
    public IEnumerable<(DocumentNode Node, int Depth)> InDocumentOrder()
    {
        if (Root is null)
        {
            yield break;
        }

        var visited = new HashSet<Guid>();
        var stack = new Stack<(DocumentNode Node, int Depth)>();
        stack.Push((Root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (!visited.Add(node.Id))
            {
                continue; // a cycle; the validator reports it
            }

            yield return (node, depth);

            var children = ChildrenOf(node.Id);
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], depth + 1));
            }
        }
    }
}
