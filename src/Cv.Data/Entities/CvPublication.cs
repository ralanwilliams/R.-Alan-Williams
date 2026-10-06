namespace Cv.Data.Entities;

/// <summary>
/// Append-only log entry: from now on <see cref="LocaleCode"/> serves <see cref="VersionId"/>
/// (or nothing, when <see cref="VersionId"/> is null). The latest row per locale wins.
/// </summary>
public sealed class CvPublication
{
    public long Seq { get; private set; }
    public required string LocaleCode { get; init; }

    /// <summary>Always true. Part of the composite foreign key that only matches publishable locales.</summary>
    public bool LocalePublishable { get; init; } = true;

    public Guid? VersionId { get; init; }
    public required Guid PublishedBy { get; init; }
    public DateTimeOffset PublishedAt { get; private set; }
    public string? Note { get; init; }
}
