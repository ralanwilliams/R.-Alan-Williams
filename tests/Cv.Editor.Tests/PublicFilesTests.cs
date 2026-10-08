using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cv.Core.Drafts;
using Cv.Core.Rendering;
using Cv.Data.Store;
using Cv.Editor.Publishing;
using Cv.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cv.Editor.Tests;

/// <summary>
/// Publishing renders and stores the public files first (ADR 0003 §2), and the backfill command
/// stores them for versions published earlier. The in-memory store refuses a publication without
/// files, as the database does.
/// </summary>
public sealed class PublicFilesTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<(HttpResponseMessage Response, JsonNode Body)> PostAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Json);
        return (response, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    private static async Task<Guid> SaveAsync(HttpClient client, Guid? baseVersionId, Draft document, params string[] publish)
    {
        var (response, body) = await PostAsync(client, "/api/versions", new { baseVersionId, document, publish });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return body["version"]!["id"]!.GetValue<Guid>();
    }

    private static Task<(HttpResponseMessage Response, JsonNode Body)> PublishAsync(HttpClient client, Guid? versionId, params string[] locales) =>
        PostAsync(client, "/api/publications", new { versionId, locales });

    private static string Text(RenderedFile file) => Encoding.UTF8.GetString(file.Content);

    private static List<string> Keys(InMemoryCvStore store) =>
        store.Files.Select(f => $"{f.File.Locale}.{f.File.Format}").Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Save_and_publish_stores_every_format_for_each_published_language()
    {
        await using var app = new EditorFactory();

        var v1 = await SaveAsync(app.CreateEditorClient(), null, TestDraft.Sample().Draft.ToDraft(), "en", "nb");

        Assert.Equal(["en.html", "en.md", "en.pdf", "nb.html", "nb.md", "nb.pdf"], Keys(app.Store));
        Assert.All(app.Store.Files, f => Assert.Equal(v1, f.VersionId));

        var html = app.Store.Files.Single(f => f.File is { Locale: "nb", Format: "html" }).File;
        Assert.Equal(HtmlRenderer.RendererVersion, html.RendererVersion);
        Assert.Contains("lang=\"nb\"", Text(html));
        Assert.Contains("<style>", Text(html));                 // standalone
        Assert.DoesNotContain("data-node-id=", Text(html));     // not the preview
        Assert.Contains("Programvareutvikler", Text(html));

        var md = app.Store.Files.Single(f => f.File is { Locale: "en", Format: "md" }).File;
        Assert.Equal(MarkdownRenderer.RendererVersion, md.RendererVersion);
        Assert.Contains("Software engineer", Text(md));

        var pdf = app.Store.Files.Single(f => f.File is { Locale: "en", Format: "pdf" }).File;
        Assert.Equal("%PDF-1.7 fake", Text(pdf));
        Assert.Equal(2, app.Pdf.Count);
    }

    [Fact]
    public async Task Saving_without_publishing_stores_no_files()
    {
        await using var app = new EditorFactory();

        await SaveAsync(app.CreateEditorClient(), null, TestDraft.Sample().Draft.ToDraft());

        Assert.Empty(app.Store.Files);
        Assert.Equal(0, app.Pdf.Count);
    }

    [Fact]
    public async Task Publishing_a_saved_version_renders_its_files_once()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var v1 = await SaveAsync(client, null, TestDraft.Sample().Draft.ToDraft());

        var (first, _) = await PublishAsync(client, v1, "en");
        var (unpublish, _) = await PublishAsync(client, null, "en");
        var (again, _) = await PublishAsync(client, v1, "en");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(["en.html", "en.md", "en.pdf"], Keys(app.Store)); // republishing found its files
        Assert.Equal(1, app.Pdf.Count);
    }

    [Fact]
    public async Task Publishing_another_language_renders_only_that_language()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var draft = TestDraft.Sample().Draft.ToDraft();
        var v1 = await SaveAsync(client, null, draft, "en");

        // Save & publish with nothing new to save publishes the base version.
        var (response, _) = await PostAsync(client, "/api/versions", new { baseVersionId = v1, document = draft, publish = new[] { "en", "fr" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["en.html", "en.md", "en.pdf", "fr.html", "fr.md", "fr.pdf"], Keys(app.Store));
        Assert.Equal(2, app.Pdf.Count);
    }

    [Fact]
    public async Task Files_from_an_older_renderer_are_rendered_again_on_publish()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var v1 = await SaveAsync(client, null, TestDraft.Sample().Draft.ToDraft());
        await app.Store.AddFilesAsync(v1, [.. new[] { "pdf", "html", "md" }.Select(f => new RenderedFile("en", f, "old/1", "old"u8.ToArray()))]);

        var (response, _) = await PublishAsync(client, v1, "en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, app.Store.Files.Count); // the old ones stay: cv_renders is append-only
        Assert.Equal(3, app.Store.Files.Count(f => f.File.RendererVersion != "old/1"));
    }

    [Fact]
    public async Task Without_a_browser_nothing_is_saved_or_published()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var v1 = await SaveAsync(client, null, TestDraft.Sample().Draft.ToDraft());
        app.Pdf.Unavailable = true;

        var (publish, problem) = await PublishAsync(client, v1, "en");
        var (save, _) = await PostAsync(client, "/api/versions", new
        {
            baseVersionId = v1,
            document = Edit(TestDraft.Sample().Draft.ToDraft(), "Principal engineer"),
            publish = new[] { "en" },
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, publish.StatusCode);
        Assert.Equal("Cannot create PDFs", problem["title"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, save.StatusCode);
        Assert.Single(await app.Store.ListVersionsAsync());
        Assert.Empty(await app.Store.GetPublishedAsync());
        Assert.Empty(app.Store.Files);
    }

    [Fact]
    public async Task The_backfill_stores_current_files_for_every_version_ever_published()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var sample = TestDraft.Sample().Draft;
        var v1 = await SaveAsync(client, null, sample.ToDraft());
        var v2 = await SaveAsync(client, v1, Edit(sample.ToDraft(), "Principal engineer"));
        var v3 = await SaveAsync(client, v2, Edit(sample.ToDraft(), "Distinguished engineer")); // a draft: never published

        // Published with files from an older renderer, as if before a RendererVersion bump.
        static RenderedFile[] Old(string locale) => [.. new[] { "pdf", "html", "md" }.Select(f => new RenderedFile(locale, f, "old/1", "old"u8.ToArray()))];
        Assert.IsType<PublishResult.Published>(await app.Store.PublishAsync(new PublishRequest(v1, ["en", "nb"], InMemoryCvStore.Author.Id, Files: [.. Old("en"), .. Old("nb")])));
        Assert.IsType<PublishResult.Published>(await app.Store.PublishAsync(new PublishRequest(v2, ["en"], InMemoryCvStore.Author.Id, Files: Old("en"))));
        Assert.IsType<PublishResult.Published>(await app.Store.PublishAsync(new PublishRequest(null, ["nb"], InMemoryCvStore.Author.Id))); // permalinks still serve it
        var files = app.Services.GetRequiredService<PublicFiles>();

        var planned = await files.BackfillAsync(dryRun: true, progress: null, CancellationToken.None);
        Assert.Equal(9, app.Store.Files.Count); // a dry run stores nothing

        var stored = await files.BackfillAsync(dryRun: false, progress: null, CancellationToken.None);
        var again = await files.BackfillAsync(dryRun: false, progress: null, CancellationToken.None);

        string[] expected = ["v1 en: pdf, html, md", "v1 nb: pdf, html, md", "v2 en: pdf, html, md"];
        Assert.Equal(expected, planned.Select(i => $"v{i.VersionNumber} {i.Locale}: {string.Join(", ", i.Formats)}"));
        Assert.Equal(expected, stored.Select(i => $"v{i.VersionNumber} {i.Locale}: {string.Join(", ", i.Formats)}"));
        Assert.Empty(again);
        Assert.Equal(18, app.Store.Files.Count);
        Assert.DoesNotContain(app.Store.Files, f => f.VersionId == v3);
        Assert.Equal(3, app.Pdf.Count);
    }

    private static Draft Edit(Draft draft, string headline)
    {
        var node = draft.Nodes.Single(n => n.Type == "headline").Id;
        return draft with
        {
            Texts = draft.Texts.Select(t => t.NodeId == node && t.Locale == "en" ? t with { Content = headline } : t).ToList(),
        };
    }
}
