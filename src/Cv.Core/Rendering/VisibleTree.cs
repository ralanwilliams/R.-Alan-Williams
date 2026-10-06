using Cv.Core.Localization;

namespace Cv.Core.Rendering;

/// <summary>
/// The tree a renderer walks: each node's children that are not hidden in this locale, in
/// document order. Shared by <see cref="HtmlRenderer"/> and <see cref="MarkdownRenderer"/>.
/// </summary>
internal sealed class VisibleTree
{
    private readonly Dictionary<Guid, List<LocalizedNode>> _children = [];

    public VisibleTree(LocalizedDocument document)
    {
        foreach (var node in document.Nodes)
        {
            if (node.Node.ParentId is { } parentId && !node.IsHidden)
            {
                if (!_children.TryGetValue(parentId, out var list))
                {
                    _children[parentId] = list = [];
                }
                list.Add(node); // document order is preserved
            }
        }
        Root = document.Nodes.FirstOrDefault(n => n.Depth == 0) is { IsHidden: false } root ? root : null;
    }

    /// <summary>The root, or null when the document is empty or hidden in this locale.</summary>
    public LocalizedNode? Root { get; }

    public IReadOnlyList<LocalizedNode> ChildrenOf(Guid id) => _children.GetValueOrDefault(id) ?? [];

    /// <summary>The name, headline and contacts at the top of the root, which form the header.</summary>
    public static bool IsHeader(LocalizedNode node) => node.Node.Type is "name" or "headline" or "contact";

    /// <summary>Header types are pulled out of an entry's children into its two header lines.</summary>
    public static bool IsEntryHeader(LocalizedNode node) => node.Node.Type is "subtitle" or "location";
}
