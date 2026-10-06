using Cv.Core.Hashing;
using Cv.Core.Model;

namespace Cv.Data.Store;

/// <summary>
/// Reads and writes CV versions. Every write appends rows; nothing is updated or deleted
/// (ADR 0001 §1). Connects as a member of <c>cv_app</c>, which can only SELECT and INSERT.
/// </summary>
public interface ICvStore
{
    /// <summary>Locales and grammar. Seed data that only a migration changes, so it is loaded once.</summary>
    Task<CvCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>The user with this email, or with no email given, the only user if there is exactly one.</summary>
    Task<CvUser?> FindUserAsync(string? email, CancellationToken cancellationToken = default);

    Task<VersionInfo?> GetLatestVersionAsync(CancellationToken cancellationToken = default);

    Task<VersionInfo?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The full document of a version. Throws <see cref="KeyNotFoundException"/> for an unknown id.</summary>
    Task<CvDocument> LoadDocumentAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>What each locale serves right now (<c>cv.cv_published</c>). Unpublished locales are absent.</summary>
    Task<IReadOnlyList<PublishedLocale>> GetPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a version as the successor of <see cref="NewVersion.BaseVersionId"/> and, in the
    /// same transaction, publishes it for <see cref="NewVersion.PublishLocales"/>. All or nothing.
    /// </summary>
    Task<SaveResult> SaveVersionAsync(NewVersion version, CancellationToken cancellationToken = default);

    /// <summary>Points each locale at a version, or at nothing when <see cref="PublishRequest.VersionId"/> is null. All or nothing.</summary>
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>What changed from one version to another (<c>cv.cv_version_diff</c>).</summary>
    Task<IReadOnlyList<VersionChange>> DiffAsync(Guid fromVersionId, Guid toVersionId, CancellationToken cancellationToken = default);
}

public sealed record CvUser(Guid Id, string Email, string DisplayName);

public sealed record VersionInfo(
    Guid Id,
    int Number,
    Guid? PreviousVersionId,
    Guid? RestoredFromVersionId,
    string? Summary,
    Sha256Digest ContentHash,
    DateTimeOffset CreatedAt);

public sealed record PublishedLocale(string Locale, Guid VersionId, DateTimeOffset PublishedAt);

public sealed record NewVersion(
    Guid? BaseVersionId,
    CvDocument Document,
    Guid CreatedBy,
    string? Summary = null,
    Guid? RestoredFromVersionId = null,
    IReadOnlyList<string>? PublishLocales = null);

public abstract record SaveResult
{
    private SaveResult() { }

    public sealed record Saved(VersionInfo Version) : SaveResult;

    /// <summary>
    /// The base is no longer the latest version: another tab (or the history panel) saved in
    /// between. The editor must reload instead of retrying (ADR 0001 §3).
    /// </summary>
    public sealed record Conflict(VersionInfo? Latest) : SaveResult;

    /// <summary>The content is identical to the base version, so there is nothing to save.</summary>
    public sealed record NoChanges : SaveResult;

    /// <summary>The database refused the version or a publication; <see cref="Message"/> says why.</summary>
    public sealed record Rejected(string Message) : SaveResult;
}

public sealed record PublishRequest(Guid? VersionId, IReadOnlyList<string> Locales, Guid PublishedBy, string? Note = null);

public abstract record PublishResult
{
    private PublishResult() { }

    public sealed record Published : PublishResult;

    public sealed record Rejected(string Message) : PublishResult;
}

/// <summary>One row of <c>cv.cv_version_diff</c>. <see cref="Locale"/> is null for structural changes.</summary>
public sealed record VersionChange(Guid NodeId, string? Locale, string Change, string? OldValue, string? NewValue);
