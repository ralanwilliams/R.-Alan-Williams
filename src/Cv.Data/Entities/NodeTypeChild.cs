namespace Cv.Data.Entities;

/// <summary>One rule of the document grammar: a <see cref="ParentType"/> node may contain <see cref="ChildType"/> nodes.</summary>
public sealed class NodeTypeChild
{
    public required string ParentType { get; init; }
    public required string ChildType { get; init; }
}
