namespace Cv.Data.Entities;

/// <summary>
/// One node of the CV tree within a version. <see cref="NodeId"/> is stable across versions,
/// which is what makes diffs and restores work. The tree is shared by all languages.
/// </summary>
public sealed class CvNode
{
    public required Guid VersionId { get; init; }
    public required Guid NodeId { get; init; }

    /// <summary>Null only for the root.</summary>
    public Guid? ParentNodeId { get; init; }

    public required string TypeCode { get; init; }

    /// <summary>Fractional index (base62) ordering siblings; compared with the "C" collation.</summary>
    public required string SortKey { get; init; }

    /// <summary>Language-neutral data as a JSON object, e.g. <c>{"start":"2021-03","end":null}</c>.</summary>
    public string Attrs { get; init; } = "{}";
}
