namespace Cv.Data.Entities;

/// <summary>Kind of CV node (root, section, entry, bullet...). Drives rendering and the document grammar.</summary>
public sealed class NodeType
{
    public const string Root = "root";

    public required string Code { get; init; }
    public required string Description { get; set; }

    /// <summary>False for purely structural nodes (the root), which carry no text.</summary>
    public bool HasText { get; init; }
}
