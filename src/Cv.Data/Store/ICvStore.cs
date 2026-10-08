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
    /// same transaction, stores <see cref="NewVersion.Files"/> and publishes it for
    /// <see cref="NewVersion.PublishLocales"/>. All or nothing.
    /// </summary>
    Task<SaveResult> SaveVersionAsync(NewVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <see cref="PublishRequest.Files"/>, then points each locale at the version, or at
    /// nothing when <see cref="PublishRequest.VersionId"/> is null. All or nothing.
    /// </summary>
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>The files stored for a version (<c>cv.cv_renders</c>), without their content.</summary>
    Task<IReadOnlyList<StoredFile>> ListFilesAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every version and locale that has ever been published, oldest version first. Includes
    /// locales that were unpublished later, because their permalinks still serve them (ADR 0003 §3).
    /// </summary>
    Task<IReadOnlyList<PublishedOnce>> ListEverPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores files for an existing version in one transaction, without publishing anything.</summary>
    Task AddFilesAsync(Guid versionId, IReadOnlyList<RenderedFile> files, CancellationToken cancellationToken = default);

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

/// <param name="Files">Files to store with the version. Publishing needs every format for each locale (ADR 0003 §2).</param>
public sealed record NewVersion(
    Guid? BaseVersionId,
    CvDocument Document,
    Guid CreatedBy,
    string? Summary = null,
    Guid? RestoredFromVersionId = null,
    IReadOnlyList<string>? PublishLocales = null,
    IReadOnlyList<RenderedFile>? Files = null);

/// <summary>
/// A rendered file to store in <c>cv.cv_renders</c>. <see cref="Format"/> is one of the codes on
/// <see cref="Entities.CvRender"/>. The store computes the SHA-256.
/// </summary>
public sealed record RenderedFile(string Locale, string Format, string RendererVersion, byte[] Content);

/// <summary>A stored file's key, without its content.</summary>
public sealed record StoredFile(string Locale, string Format, string RendererVersion);

/// <summary>A version that was published for a locale at some point.</summary>
public sealed record PublishedOnce(Guid VersionId, int VersionNumber, string Locale);

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

/// <param name="Files">Files the version doesn't have yet, stored before publishing (ADR 0003 §2). Unpublishing needs none.</param>
public sealed record PublishRequest(
    Guid? VersionId,
    IReadOnlyList<string> Locales,
    Guid PublishedBy,
    string? Note = null,
    IReadOnlyList<RenderedFile>? Files = null);

public abstract record PublishResult
{
    private PublishResult() { }

    public sealed record Published : PublishResult;

    public sealed record Rejected(string Message) : PublishResult;
}

/// <summary>One row of <c>cv.cv_version_diff</c>. <see cref="Locale"/> is null for structural changes.</summary>
public sealed record VersionChange(Guid NodeId, string? Locale, string Change, string? OldValue, string? NewValue);
