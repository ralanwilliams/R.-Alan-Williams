using Cv.Core.Model;

namespace Cv.Core.Drafts;

/// <summary>A node as the editor sends it. Sibling order is the order of appearance in <see cref="Draft.Nodes"/>.</summary>
public sealed record DraftNode(Guid Id, Guid? ParentId, string Type, IReadOnlyDictionary<string, string>? Attrs = null);

/// <summary>
/// One line's text in one locale as the editor sends it.
/// <see cref="Reviewed"/> means "this translation is up to date with the current source text",
/// for a translation the author checked without changing it.
/// </summary>
public sealed record DraftText(Guid NodeId, string Locale, string? Content, bool Omitted = false, bool Reviewed = false);

/// <summary>
/// The editor's working copy of the CV. Unlike <see cref="CvDocument"/> it has no sort keys or
/// source hashes: those are derived on the server, from the base version, by <see cref="DraftBuilder"/>.
/// </summary>
public sealed record Draft(IReadOnlyList<DraftNode> Nodes, IReadOnlyList<DraftText> Texts)
{
    /// <summary>The editor's form of a stored document: nodes in document order, texts as stored.</summary>
    public static Draft FromDocument(CvDocument document) => new(
        document.InDocumentOrder()
            .Select(x => new DraftNode(x.Node.Id, x.Node.ParentId, x.Node.Type, x.Node.Attrs))
            .ToArray(),
        document.Texts
            .Select(t => new DraftText(t.NodeId, t.Locale, t.Content, t.IsOmitted))
            .ToArray());
}
