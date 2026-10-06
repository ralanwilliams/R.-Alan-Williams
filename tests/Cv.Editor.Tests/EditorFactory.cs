using System.Net;
using Cv.Core.Hashing;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Data.Store;
using Cv.Editor;
using Cv.Editor.Pdf;
using Cv.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ICvStore>(Store);
            services.AddSingleton<IPdfRenderer>(Pdf);
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
    public string? LastHtml { get; private set; }

    public Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        LastHtml = html;
        return Task.FromResult("%PDF-1.7 fake"u8.ToArray());
    }
}

/// <summary>An <see cref="ICvStore"/> with the same contract as the database where the API depends on it.</summary>
public sealed class InMemoryCvStore : ICvStore
{
    private readonly object _lock = new();
    private readonly List<(VersionInfo Info, CvDocument Document)> _versions = [];
    private readonly Dictionary<string, PublishedLocale> _published = new(StringComparer.Ordinal);

    public static readonly CvUser Author = new(Guid.Parse("0199a0a0-0000-7000-8000-000000000001"), "ada@example.com", "Ada Lovelace");

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
            _versions.Add((info, version.Document));
            foreach (var locale in version.PublishLocales ?? [])
            {
                _published[locale] = new PublishedLocale(locale, info.Id, DateTimeOffset.UtcNow);
            }
            return Task.FromResult<SaveResult>(new SaveResult.Saved(info));
        }
    }

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            foreach (var locale in request.Locales)
            {
                if (request.VersionId is { } id)
                {
                    _published[locale] = new PublishedLocale(locale, id, DateTimeOffset.UtcNow);
                }
                else
                {
                    _published.Remove(locale);
                }
            }
            return Task.FromResult<PublishResult>(new PublishResult.Published());
        }
    }

    public Task<IReadOnlyList<VersionChange>> DiffAsync(Guid fromVersionId, Guid toVersionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VersionChange>>([]);
}
