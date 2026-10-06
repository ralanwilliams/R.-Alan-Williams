namespace Cv.Data.Entities;

/// <summary>The text of one node in one locale for one version.</summary>
public sealed class CvNodeContent
{
    public required Guid VersionId { get; init; }
    public required Guid NodeId { get; init; }
    public required string LocaleCode { get; init; }

    /// <summary>Null exactly when <see cref="IsOmitted"/> is true.</summary>
    public string? Content { get; init; }

    /// <summary>Deliberately left out of this locale. Hides the node and its whole subtree there.</summary>
    public bool IsOmitted { get; init; }

    /// <summary>SHA-256 of the source-locale text this translation was made from; used to detect stale translations.</summary>
    public byte[]? SourceHash { get; init; }
}
