using System.Net;
using System.Security.Cryptography;
using Cv.Core.Hashing;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Data.Store;
using Cv.Editor;
using Cv.Editor.Pdf;
using Cv.Editor.Security;
using Cv.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cv.Editor.Tests;

/// <summary>
/// The real editor app (same pipeline, middleware and endpoints) with the database and the
/// browser replaced. Requests come from 127.0.0.1 unless a test sets <see cref="RemoteAddressHeader"/>.
/// </summary>
public sealed class EditorFactory : WebApplicationFactory<Program>
{
    public const string RemoteAddressHeader = "X-Test-Remote-Address";
    public const string Origin = "http://localhost";

    public InMemoryCvStore Store { get; } = new();
    public FakePdfRenderer Pdf { get; } = new();

    /// <summary>Sets CV_SEED_FILE for this app instance.</summary>
    public string? SeedFile { get; init; }

    /// <summary>Turns on remote access (ADR 0004) with <see cref="TestAccess"/>'s team, audience and key.</summary>
    public bool RemoteAccess { get; init; }

    /// <summary>Stands in for Cloudflare Access: its key signs the tokens the tests send.</summary>
    public TestAccess Access { get; } = new();

    /// <summary>A client whose requests carry the editor's own Origin, as the page's do.</summary>
    public HttpClient CreateEditorClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(EditorOptions.ConnectionStringVariable, "Host=never-used.invalid");
        if (SeedFile is not null)
        {
            builder.UseSetting(EditorOptions.SeedFileVariable, SeedFile);
        }
        if (RemoteAccess)
        {
            builder.UseSetting(EditorOptions.PublicHostVariable, TestAccess.PublicHost);
            builder.UseSetting(EditorOptions.AccessTeamDomainVariable, TestAccess.TeamDomain);
            builder.UseSetting(EditorOptions.AccessAudienceVariable, TestAccess.Audience);
        }
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ICvStore>(Store);
            services.AddSingleton<IPdfRenderer>(Pdf);
            services.AddSingleton<IAccessKeySource>(Access);
            services.AddSingleton<IStartupFilter, TestRemoteAddress>();
        });
    }

    /// <summary>TestServer has no socket, so it reports no peer address; supply one before the app's middleware runs.</summary>
    private sealed class TestRemoteAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress =
                    IPAddress.TryParse(context.Request.Headers[RemoteAddressHeader], out var address) ? address : IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class FakePdfRenderer : IPdfRenderer
{
    private int _count;

    public string? LastHtml { get; private set; }

    /// <summary>How many PDFs were printed.</summary>
    public int Count => _count;

    /// <summary>When set, printing fails as it does on a computer without a browser.</summary>
    public bool Unavailable { get; set; }

    public Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new PdfUnavailableException("No Chrome, Edge or Chromium found.");
        }
        LastHtml = html;
        Interlocked.Increment(ref _count);
        return Task.FromResult("%PDF-1.7 fake"u8.ToArray());
    }
}

/// <summary>An <see cref="ICvStore"/> with the same contract as the database where the API depends on it.</summary>
public sealed class InMemoryCvStore : ICvStore
{
    private readonly object _lock = new();
    private readonly List<(VersionInfo Info, CvDocument Document)> _versions = [];
    private readonly Dictionary<string, PublishedLocale> _published = new(StringComparer.Ordinal);
    private readonly List<(Guid VersionId, string Locale)> _publications = [];
    private readonly List<(Guid VersionId, RenderedFile File)> _files = [];

    public static readonly CvUser Author = new(Guid.Parse("0199a0a0-0000-7000-8000-000000000001"), "ada@example.com", "Ada Lovelace");

    /// <summary>Every stored file, oldest first.</summary>
    public IReadOnlyList<(Guid VersionId, RenderedFile File)> Files
    {
        get
        {
            lock (_lock)
            {
                return _files.ToList();
            }
        }
    }

    public Task<CvCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => Task.FromResult(TestDocuments.Catalog);

    public Task<CvUser?> FindUserAsync(string? email, CancellationToken cancellationToken = default) => Task.FromResult<CvUser?>(Author);

    public Task<VersionInfo?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<VersionInfo?>(_versions.LastOrDefault().Info);
        }
    }

    public Task<VersionInfo?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<VersionInfo?>(_versions.FirstOrDefault(v => v.Info.Id == versionId).Info);
        }
    }

    public Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<VersionInfo>>(_versions.Select(v => v.Info).Reverse().ToList());
        }
    }

    public Task<CvDocument> LoadDocumentAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return _versions.FirstOrDefault(v => v.Info.Id == versionId).Document is { } document
                ? Task.FromResult(document)
                : throw new KeyNotFoundException();
        }
    }

    public Task<IReadOnlyList<PublishedLocale>> GetPublishedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PublishedLocale>>(_published.Values.ToList());
        }
    }

    public Task<SaveResult> SaveVersionAsync(NewVersion version, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var latest = _versions.LastOrDefault().Info;
            if (version.BaseVersionId != latest?.Id)
            {
                return Task.FromResult<SaveResult>(new SaveResult.Conflict(latest));
            }

            var hash = ContentHash.Compute(version.Document);
            if (latest is not null && latest.ContentHash == hash)
            {
                return Task.FromResult<SaveResult>(new SaveResult.NoChanges());
            }

            foreach (var locale in version.PublishLocales ?? [])
            {
                if (!Localizer.Localize(version.Document, TestDocuments.Catalog, locale).Readiness.CanPublish)
                {
                    return Task.FromResult<SaveResult>(new SaveResult.Rejected($"Cannot publish locale {locale}"));
                }
            }

            var info = new VersionInfo(Guid.CreateVersion7(), (latest?.Number ?? 0) + 1, latest?.Id, version.RestoredFromVersionId,
                version.Summary, hash, DateTimeOffset.UtcNow);
            if (CheckFiles(info.Id, version.PublishLocales ?? [], version.Files ?? []) is { } refused)
            {
                return Task.FromResult<SaveResult>(new SaveResult.Rejected(refused));
            }

            _versions.Add((info, version.Document));
            _files.AddRange((version.Files ?? []).Select(f => (info.Id, f)));
            foreach (var locale in version.PublishLocales ?? [])
            {
                _published[locale] = new PublishedLocale(locale, info.Id, DateTimeOffset.UtcNow);
                _publications.Add((info.Id, locale));
            }
            return Task.FromResult<SaveResult>(new SaveResult.Saved(info));
        }
    }

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (request.VersionId is { } versionId && CheckFiles(versionId, request.Locales, request.Files ?? []) is { } refused)
            {
                return Task.FromResult<PublishResult>(new PublishResult.Rejected(refused));
            }

            foreach (var locale in request.Locales)
            {
                if (request.VersionId is { } id)
                {
                    _published[locale] = new PublishedLocale(locale, id, DateTimeOffset.UtcNow);
                    _publications.Add((id, locale));
                }
                else
                {
                    _published.Remove(locale);
                }
            }
            if (request.VersionId is { } withFiles)
            {
                _files.AddRange((request.Files ?? []).Select(f => (withFiles, f)));
            }
            return Task.FromResult<PublishResult>(new PublishResult.Published());
        }
    }

    public Task<IReadOnlyList<StoredFile>> ListFilesAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<StoredFile>>(_files
                .Where(f => f.VersionId == versionId)
                .Select(f => new StoredFile(f.File.Locale, f.File.Format, f.File.RendererVersion))
                .ToList());
        }
    }

    public Task<IReadOnlyList<PublishedOnce>> ListEverPublishedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<PublishedOnce>>(_publications
                .Distinct()
                .Select(p => new PublishedOnce(p.VersionId, _versions.Single(v => v.Info.Id == p.VersionId).Info.Number, p.Locale))
                .OrderBy(p => p.VersionNumber).ThenBy(p => p.Locale, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task AddFilesAsync(Guid versionId, IReadOnlyList<RenderedFile> files, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (CheckFiles(versionId, [], files) is { } refused)
            {
                throw new InvalidOperationException(refused);
            }
            _files.AddRange(files.Select(f => (versionId, f)));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// What the database refuses: a file whose key is already stored (pk_cv_renders), an empty file,
    /// or a publication without a file in every format (ADR 0003 §2). Null when all is well.
    /// </summary>
    private string? CheckFiles(Guid versionId, IReadOnlyList<string> publishLocales, IReadOnlyList<RenderedFile> newFiles)
    {
        var stored = _files.Where(f => f.VersionId == versionId).Select(f => f.File).ToList();
        if (newFiles.FirstOrDefault(n => n.Content.Length == 0 || stored.Any(s => (s.Locale, s.Format, s.RendererVersion) == (n.Locale, n.Format, n.RendererVersion))) is { } bad)
        {
            return $"Refused file {bad.Locale}/{bad.Format}/{bad.RendererVersion}";
        }

        foreach (var locale in publishLocales)
        {
            var missing = new[] { "html", "md", "pdf" }.Where(format => !stored.Concat(newFiles).Any(f => f.Locale == locale && f.Format == format)).ToList();
            if (missing.Count > 0)
            {
                return $"Cannot publish locale {locale}: no stored file in format(s) {string.Join(", ", missing)}";
            }
        }
        return null;
    }

    public Task<IReadOnlyList<VersionChange>> DiffAsync(Guid fromVersionId, Guid toVersionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VersionChange>>([]);
}

/// <summary>
/// Cloudflare Access for tests: a Zero Trust team with signing keys. <see cref="CreateToken"/>
/// mints the token Access would add as <c>Cf-Access-Jwt-Assertion</c>.
/// </summary>
public sealed class TestAccess : IAccessKeySource
{
    public const string PublicHost = "editor.example.com";
    public const string TeamDomain = "team.cloudflareaccess.com";
    public const string Audience = "0123456789abcdef-test-aud";

    private readonly List<SecurityKey> _published;
    private List<SecurityKey> _fetched;

    public TestAccess()
    {
        Key = NewKey("key-1");
        _published = [Key];
        _fetched = [.. _published];
    }

    /// <summary>The key Access currently signs with.</summary>
    public RsaSecurityKey Key { get; private set; }

    /// <summary>How often the editor fetched the keys again because a token named an unknown one.</summary>
    public int Refreshes { get; private set; }

    public static RsaSecurityKey NewKey(string id) => new(RSA.Create(2048)) { KeyId = id };

    /// <summary>Access starts signing with a new key, as it does every 6 weeks. The editor hasn't fetched it yet.</summary>
    public void Rotate()
    {
        Key = NewKey($"key-{_published.Count + 1}");
        _published.Add(Key);
    }

    public Task<ICollection<SecurityKey>> GetKeysAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (refresh)
        {
            Refreshes++;
            _fetched = [.. _published];
        }
        return Task.FromResult<ICollection<SecurityKey>>(_fetched);
    }

    public string CreateToken(
        string email = "ada@example.com",
        string audience = Audience,
        string issuer = $"https://{TeamDomain}",
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiry.AddHours(-2),
            NotBefore = expiry.AddHours(-2),
            Expires = expiry,
            Claims = new Dictionary<string, object> { ["email"] = email },
            SigningCredentials = new SigningCredentials(signingKey ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }
}
