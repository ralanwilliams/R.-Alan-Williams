namespace Cv.Data.Entities;

/// <summary>
/// An immutable snapshot of the whole CV (structure and every locale). Every save creates one.
/// History is linear: each version has at most one successor.
/// </summary>
public sealed class CvVersion
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required int VersionNumber { get; init; }
    public Guid? PreviousVersionId { get; init; }
    public Guid? RestoredFromVersionId { get; init; }
    public string? Summary { get; init; }

    /// <summary>SHA-256 over structure and all locales; the database rejects a save identical to its predecessor.</summary>
    public required byte[] ContentHash { get; init; }

    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; private set; }
}
