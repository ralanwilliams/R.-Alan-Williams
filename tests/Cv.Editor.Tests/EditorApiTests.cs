using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cv.Core.Drafts;
using Cv.Testing;

namespace Cv.Editor.Tests;

public sealed class EditorApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static async Task<(HttpResponseMessage Response, JsonNode Body)> PostAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Json);
        return (response, await ReadAsync(response));
    }

    private static Task<(HttpResponseMessage Response, JsonNode Body)> SaveAsync(
        HttpClient client, Guid? baseVersionId, Draft document, string[]? publish = null, bool confirmStale = false) =>
        PostAsync(client, "/api/versions", new { baseVersionId, document, summary = (string?)null, publish, confirmStale });

    private static Guid VersionId(JsonNode body) => body["version"]!["id"]!.GetValue<Guid>();

    private static Draft Edit(Draft draft, Guid node, string locale, string content) => draft with
    {
        Texts = draft.Texts.Select(t => t.NodeId == node && t.Locale == locale ? t with { Content = content } : t).ToList(),
    };

    [Fact]
    public async Task The_session_describes_the_author_languages_and_grammar()
    {
        await using var app = new EditorFactory();

        var session = await ReadAsync(await app.CreateClient().GetAsync("/api/session"));

        Assert.Equal("Ada Lovelace", session["user"]!["displayName"]!.GetValue<string>());
        Assert.Equal("en", session["sourceLocale"]!.GetValue<string>());
        Assert.Contains(session["locales"]!.AsArray(), l => l!["code"]!.GetValue<string>() == "nb");
        var root = session["nodeTypes"]!.AsArray().Single(t => t!["code"]!.GetValue<string>() == "root")!;
        Assert.Equal(["name", "headline", "contact", "section"], root["children"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal("yearMonth", session["attributes"]!["entry"]![0]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_empty_database_opens_the_starter_document()
    {
        await using var app = new EditorFactory();

        var current = await ReadAsync(await app.CreateClient().GetAsync("/api/document"));

        Assert.Null(current["version"]);
        var texts = current["document"]!["texts"]!.AsArray();
        Assert.Contains(texts, t => t!["content"]?.GetValue<string>() == "Ada Lovelace" && t["locale"]!.GetValue<string>() == "zxx");
    }

    [Fact]
    public async Task An_empty_database_opens_the_seed_file_when_one_is_configured()
    {
        var seed = Path.Combine(Path.GetTempPath(), $"cv-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(seed, """
            // a seed file may have comments
            { "children": [ { "type": "name", "zxx": "Ada Lovelace" }, { "type": "section", "en": "Experience" } ] }
            """);
        try
        {
            await using var app = new EditorFactory { SeedFile = seed };

            var current = await ReadAsync(await app.CreateClient().GetAsync("/api/document"));

            Assert.Null(current["version"]);
            Assert.Equal(Path.GetFileName(seed), current["seedFile"]!.GetValue<string>());
            Assert.Equal(["root", "name", "section"], current["document"]!["nodes"]!.AsArray().Select(n => n!["type"]!.GetValue<string>()));
            Assert.Empty(await app.Store.ListVersionsAsync()); // opening it saves nothing
        }
        finally
        {
            File.Delete(seed);
        }
    }

    [Fact]
    public async Task A_broken_seed_file_is_reported_with_the_path_of_the_problem()
    {
        var seed = Path.Combine(Path.GetTempPath(), $"cv-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(seed, """{ "children": [ { "type": "name", "zxx": "Ada", "tittle": "x" } ] }""");
        try
        {
            await using var app = new EditorFactory { SeedFile = seed };

            var response = await app.CreateClient().GetAsync("/api/document");
            var problem = await ReadAsync(response);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("$.children[0].tittle: unknown property", problem["detail"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(seed);
        }
    }

    [Fact]
    public async Task Analysis_renders_the_preview_and_flags_what_is_missing_per_language()
    {
        await using var app = new EditorFactory();
        var d = new TestDraft();
        var section = d.Add(d.Root, "section", en: "Experience", nb: "Erfaring");

        var (response, body) = await PostAsync(app.CreateEditorClient(), "/api/analyze",
            new { baseVersionId = (Guid?)null, document = d.ToDraft(), locale = "nb" });

        response.EnsureSuccessStatusCode();
        Assert.Contains("lang=\"nb\"", body["html"]!.GetValue<string>());
        Assert.Contains("href=\"/render/cv.css\"", body["html"]!.GetValue<string>());
        Assert.Equal("missing", body["nodeStatus"]![section.ToString()]!["fr"]!.GetValue<string>());
        var fr = body["readiness"]!.AsArray().Single(r => r!["locale"]!.GetValue<string>() == "fr")!;
        Assert.False(fr["canPublish"]!.GetValue<bool>());
        Assert.True(body["hasChanges"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_second_save_from_the_same_base_is_a_conflict()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var (sample, ids) = TestDraft.Sample();
        var (first, firstBody) = await SaveAsync(client, null, sample.ToDraft());
        var v1 = VersionId(firstBody);

        var (tabA, _) = await SaveAsync(client, v1, Edit(sample.ToDraft(), ids.Headline, "en", "Principal engineer"));
        var (tabB, conflict) = await SaveAsync(client, v1, Edit(sample.ToDraft(), ids.Bullet, "en", "Something else"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, tabA.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tabB.StatusCode);
        Assert.Equal("conflict", conflict["code"]!.GetValue<string>());
        Assert.Equal(2, conflict["latest"]!["number"]!.GetValue<int>());
    }

    [Fact]
    public async Task Saving_unchanged_content_is_refused_as_no_changes()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var draft = TestDraft.Sample().Draft.ToDraft();
        var v1 = VersionId((await SaveAsync(client, null, draft)).Body);

        var (response, body) = await SaveAsync(client, v1, draft);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("no-changes", body["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_invalid_document_is_refused_with_the_problems_pinned_to_lines()
    {
        await using var app = new EditorFactory();
        var d = new TestDraft();
        var misplaced = d.Add(d.Root, "bullet", en: "A bullet directly under the root");

        var (response, body) = await SaveAsync(app.CreateEditorClient(), null, d.ToDraft());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invalid", body["code"]!.GetValue<string>());
        var issue = Assert.Single(body["issues"]!.AsArray())!;
        Assert.Equal(misplaced, issue["nodeId"]!.GetValue<Guid>());
        Assert.Equal("grammar", issue["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_language_with_missing_text_cannot_be_published()
    {
        await using var app = new EditorFactory();
        var d = new TestDraft();
        d.Add(d.Root, "section", en: "Experience");

        var (response, body) = await SaveAsync(app.CreateEditorClient(), null, d.ToDraft(), publish: ["en", "fr"]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("not-ready", body["code"]!.GetValue<string>());
        Assert.Contains("Français: 1 line without text", body["detail"]!.GetValue<string>());
        Assert.Empty(await app.Store.ListVersionsAsync()); // nothing was saved
    }

    [Fact]
    public async Task Publishing_stale_translations_needs_explicit_confirmation()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var (sample, ids) = TestDraft.Sample();
        var v1 = VersionId((await SaveAsync(client, null, sample.ToDraft())).Body);
        var loaded = (await ReadAsync(await client.GetAsync($"/api/versions/{v1}")))["document"].Deserialize<Draft>(Json)!;
        var v2 = VersionId((await SaveAsync(client, v1, Edit(loaded, ids.Bullet, "en", "Designed the engine"))).Body);

        var (unconfirmed, problem) = await PostAsync(client, "/api/publications", new { versionId = v2, locales = new[] { "fr" }, confirmStale = false });
        var (confirmed, history) = await PostAsync(client, "/api/publications", new { versionId = v2, locales = new[] { "fr" }, confirmStale = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unconfirmed.StatusCode);
        Assert.Equal("confirm-stale", problem["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains(history["published"]!.AsArray(), p => p!["locale"]!.GetValue<string>() == "fr" && p["versionNumber"]!.GetValue<int>() == 2);
    }

    [Fact]
    public async Task Save_and_publish_without_changes_publishes_the_base_version()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var draft = TestDraft.Sample().Draft.ToDraft();
        var v1 = VersionId((await SaveAsync(client, null, draft)).Body);

        var (response, body) = await SaveAsync(client, v1, draft, publish: ["en"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await app.Store.ListVersionsAsync());
        Assert.Contains(body["published"]!.AsArray(), p => p!["locale"]!.GetValue<string>() == "en");
    }

    [Fact]
    public async Task The_PDF_download_renders_the_chosen_language_with_a_UTF8_file_name()
    {
        await using var app = new EditorFactory();

        var response = await app.CreateEditorClient().PostAsJsonAsync("/api/pdf",
            new { baseVersionId = (Guid?)null, document = TestDraft.Sample().Draft.ToDraft(), locale = "fr" }, Json);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Ada Lovelace – CV (fr).pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Contains("lang=\"fr\"", app.Pdf.LastHtml);
        Assert.Contains("<style>", app.Pdf.LastHtml); // the PDF is self-contained
        Assert.DoesNotContain("data-node-id=", app.Pdf.LastHtml);
    }

    [Fact]
    public async Task Restoring_an_old_version_appends_it_as_the_newest()
    {
        await using var app = new EditorFactory();
        var client = app.CreateEditorClient();
        var (sample, ids) = TestDraft.Sample();
        var v1 = VersionId((await SaveAsync(client, null, sample.ToDraft())).Body);
        var v2 = VersionId((await SaveAsync(client, v1, Edit(sample.ToDraft(), ids.Headline, "en", "Principal engineer"))).Body);

        var (response, body) = await PostAsync(client, $"/api/versions/{v1}/restore", new { baseVersionId = v2 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(3, body["version"]!["number"]!.GetValue<int>());
        Assert.Equal(1, body["version"]!["restoredFromNumber"]!.GetValue<int>());
        Assert.Equal("Restored from v1", body["version"]!["summary"]!.GetValue<string>());
    }
}
