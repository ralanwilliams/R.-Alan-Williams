using System.Collections.Concurrent;
using Cv.Core.Hashing;
using Cv.Core.Model;
using Cv.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cv.Data.Store;

/// <summary>
/// <see cref="ICvStore"/> on PostgreSQL through EF Core. Register as a singleton: it holds
/// caches that are safe to share because what they cache never changes (see each field).
/// </summary>
public sealed class CvStore(IDbContextFactory<CvDbContext> contextFactory) : ICvStore
{
    private const string VersionNumberIndex = "ux_cv_versions_version_number";
    private const string PreviousVersionIndex = "ux_cv_versions_previous_version_id";

    // Locales and grammar are seed data; they change only through a migration (and a restart).
    private readonly SemaphoreSlim _catalogLock = new(1, 1);
    private CvCatalog? _catalog;

    // Versions are immutable, so a loaded document can never go stale. A CV has at most a few
    // hundred versions of a few kilobytes each, so the cache is not bounded.
    private readonly ConcurrentDictionary<Guid, CvDocument> _documents = new();

    public async Task<CvCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_catalog is { } cached)
        {
            return cached;
        }

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            if (_catalog is null)
            {
                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
                var locales = await db.Locales.AsNoTracking().ToListAsync(cancellationToken);
                var types = await db.NodeTypes.AsNoTracking().ToListAsync(cancellationToken);
                var grammar = await db.NodeTypeChildren.AsNoTracking().ToListAsync(cancellationToken);
                _catalog = new CvCatalog(
                    locales.Select(l => new LocaleInfo(l.Code, l.Name, l.IsSource, l.IsPublishable)),
                    types.Select(t => new NodeTypeInfo(t.Code, t.Description, t.HasText)),
                    grammar.Select(g => (g.ParentType, g.ChildType)));
            }
            return _catalog;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<CvUser?> FindUserAsync(string? email, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.ToLower() == normalized); // matches ux_users_email_ci
        }

        var users = await query.OrderBy(u => u.Id).Take(2).ToListAsync(cancellationToken); // two rows tell "exactly one" apart
        return users is [var user] ? new CvUser(user.Id, user.Email, user.DisplayName) : null;
    }

    public async Task<VersionInfo?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.Versions.AsNoTracking()
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : ToInfo(latest);
    }

    public async Task<VersionInfo?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var version = await db.Versions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        return version is null ? null : ToInfo(version);
    }

    public async Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var versions = await db.Versions.AsNoTracking()
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
        return versions.Select(ToInfo).ToList();
    }

    public async Task<CvDocument> LoadDocumentAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        if (_documents.TryGetValue(versionId, out var cached))
        {
            return cached;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var nodes = await db.Nodes.AsNoTracking().Where(n => n.VersionId == versionId).ToListAsync(cancellationToken);
        if (nodes.Count == 0)
        {
            // Every version has a root node (enforced by trg_cv_versions_has_root).
            throw new KeyNotFoundException($"Version {versionId} does not exist.");
        }
        var contents = await db.NodeContents.AsNoTracking().Where(c => c.VersionId == versionId).ToListAsync(cancellationToken);

        return _documents.GetOrAdd(versionId, DocumentMapper.ToDocument(nodes, contents));
    }

    public async Task<IReadOnlyList<PublishedLocale>> GetPublishedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database
            .SqlQuery<PublishedRow>($"""
                SELECT locale AS "Locale", version_id AS "VersionId", published_at AS "PublishedAt"
                FROM cv.cv_published
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new PublishedLocale(r.Locale, r.VersionId, r.PublishedAt)).OrderBy(p => p.Locale, StringComparer.Ordinal).ToList();
    }

    public async Task<SaveResult> SaveVersionAsync(NewVersion request, CancellationToken cancellationToken = default)
    {
        var contentHash = ContentHash.Compute(request.Document);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var number = 1;
        if (request.BaseVersionId is { } baseId)
        {
            var baseVersion = await db.Versions.AsNoTracking()
                .Where(v => v.Id == baseId)
                .Select(v => new { v.VersionNumber, v.ContentHash })
                .SingleOrDefaultAsync(cancellationToken);
            if (baseVersion is null)
            {
                return new SaveResult.Conflict(await GetLatestVersionAsync(cancellationToken));
            }
            if (Sha256Digest.FromBytes(baseVersion.ContentHash) == contentHash)
            {
                return new SaveResult.NoChanges(); // the database would refuse it too (trg_cv_versions_chain)
            }
            number = baseVersion.VersionNumber + 1;
        }

        var version = new CvVersion
        {
            VersionNumber = number,
            PreviousVersionId = request.BaseVersionId,
            RestoredFromVersionId = request.RestoredFromVersionId,
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            ContentHash = contentHash.ToBytes(),
            CreatedBy = request.CreatedBy,
        };
        db.Versions.Add(version);
        db.Nodes.AddRange(DocumentMapper.ToNodes(version.Id, request.Document));
        db.NodeContents.AddRange(DocumentMapper.ToContents(version.Id, request.Document));

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            // Publications go in a second batch, after the version's rows exist: the publish
            // trigger counts missing text, and EF would otherwise be free to insert the
            // publication first (it depends only on cv_versions), when the tree is still empty.
            var publishLocales = request.PublishLocales ?? [];
            if (publishLocales.Count > 0)
            {
                db.Publications.AddRange(publishLocales.Select(locale => new CvPublication
                {
                    LocaleCode = locale,
                    VersionId = version.Id,
                    PublishedBy = request.CreatedBy,
                }));
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken); // deferred constraints fire here
        }
        catch (Exception exception) when (FindPostgresException(exception) is { } postgres)
        {
            return postgres is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: VersionNumberIndex or PreviousVersionIndex }
                ? new SaveResult.Conflict(await GetLatestVersionAsync(cancellationToken))
                : new SaveResult.Rejected(Describe(postgres));
        }

        _documents.TryAdd(version.Id, request.Document);
        return new SaveResult.Saved(ToInfo(version));
    }

    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Locales.Count == 0)
        {
            return new PublishResult.Rejected("Choose at least one language.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Publications.AddRange(request.Locales.Select(locale => new CvPublication
        {
            LocaleCode = locale,
            VersionId = request.VersionId,
            PublishedBy = request.PublishedBy,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        }));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PublishResult.Published();
        }
        catch (Exception exception) when (FindPostgresException(exception) is { } postgres)
        {
            return new PublishResult.Rejected(postgres is { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "fk_cv_publications_locale" }
                ? $"Only these languages can be published: {string.Join(", ", (await GetCatalogAsync(cancellationToken)).PublishableLocales.Select(l => l.Code))}."
                : Describe(postgres));
        }
    }

    public async Task<IReadOnlyList<VersionChange>> DiffAsync(Guid fromVersionId, Guid toVersionId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database
            .SqlQuery<DiffRow>($"""
                SELECT node_id AS "NodeId", locale AS "Locale", change AS "Change",
                       old_value AS "OldValue", new_value AS "NewValue"
                FROM cv.cv_version_diff({fromVersionId}, {toVersionId})
                """)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new VersionChange(r.NodeId, r.Locale, r.Change, r.OldValue, r.NewValue)).ToList();
    }

    private static VersionInfo ToInfo(CvVersion v) => new(
        v.Id, v.VersionNumber, v.PreviousVersionId, v.RestoredFromVersionId, v.Summary,
        Sha256Digest.FromBytes(v.ContentHash), v.CreatedAt);

    private static PostgresException? FindPostgresException(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is PostgresException postgres)
            {
                return postgres;
            }
        }
        return null;
    }

    /// <summary>The database's own message: the triggers raise readable ones (ADR 0001 consequences).</summary>
    private static string Describe(PostgresException e) =>
        string.IsNullOrWhiteSpace(e.Hint) ? e.MessageText : $"{e.MessageText} {e.Hint}";

    private sealed class PublishedRow
    {
        public required string Locale { get; init; }
        public Guid VersionId { get; init; }
        public DateTimeOffset PublishedAt { get; init; }
    }

    private sealed class DiffRow
    {
        public Guid NodeId { get; init; }
        public string? Locale { get; init; }
        public required string Change { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
    }
}
