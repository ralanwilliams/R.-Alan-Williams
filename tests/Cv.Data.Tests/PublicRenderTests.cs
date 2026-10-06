using System.Security.Cryptography;
using System.Text;
using Cv.Core.Drafts;
using Cv.Core.Model;
using Cv.Data.Store;
using Cv.Testing;
using Npgsql;

namespace Cv.Data.Tests;

/// <summary>
/// What the public site can read (ADR 0003 §3): <c>cv.cv_public_render</c> as a member of
/// <c>cv_public</c>, which serves only versions that were published for the locale, and the
/// stored files' own rules.
/// </summary>
public sealed class PublicRenderTests(PostgresServer server)
{
    private static readonly Guid Author = PostgresServer.UserId;

    private sealed record PublicFile(int VersionNumber, string? Name, byte[] Content, byte[] ContentHash);

    private sealed class Fixture(TestDatabase database, CvStore store, CvCatalog catalog)
    {
        public TestDatabase Database { get; } = database;
        public CvStore Store { get; } = store;

        /// <summary>Saves the next version (headline changed to <paramref name="headline"/>), publishing it for <paramref name="publish"/>.</summary>
        public async Task<VersionInfo> SaveAsync(string headline, params string[] publish)
        {
            var latest = await Store.GetLatestVersionAsync();
            var baseDocument = latest is null ? null : await Store.LoadDocumentAsync(latest.Id);
            var draft = baseDocument is null ? TestDraft.Sample().Draft.ToDraft() : Draft.FromDocument(baseDocument);
            var headlineId = draft.Nodes.Single(n => n.Type == "headline").Id;
            draft = draft with
            {
                Texts = draft.Texts.Select(t => t.NodeId == headlineId && t.Locale == "en" ? t with { Content = headline } : t).ToList(),
            };
            var document = DraftBuilder.Build(draft, baseDocument, catalog).Document;
            var result = await Store.SaveVersionAsync(new NewVersion(latest?.Id, document, Author, PublishLocales: publish));
            return Assert.IsType<SaveResult.Saved>(result).Version;
        }

        /// <summary>Stores a file the way the editor will: as the app role, with its SHA-256.</summary>
        public async Task AddRenderAsync(Guid versionId, string locale, string format, string content, string rendererVersion = "test/1")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await using var connection = new NpgsqlConnection(Database.AppConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content)
                VALUES ($1, $2, $3, $4, $5, $6)
                """, connection);
            command.Parameters.Add(new() { Value = versionId });
            command.Parameters.Add(new() { Value = locale });
            command.Parameters.Add(new() { Value = format });
            command.Parameters.Add(new() { Value = rendererVersion });
            command.Parameters.Add(new() { Value = SHA256.HashData(bytes) });
            command.Parameters.Add(new() { Value = bytes });
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Calls the function as the public site does, as a member of cv_public.</summary>
        public async Task<PublicFile?> GetAsync(string locale, string format, int? version = null)
        {
            await using var connection = new NpgsqlConnection(Database.PublicConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT version_number, name, content, content_hash FROM cv.cv_public_render($1, $2, $3)", connection);
            command.Parameters.Add(new() { Value = locale });
            command.Parameters.Add(new() { Value = format });
            command.Parameters.Add(new() { Value = (object?)version ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer });
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }
            return new PublicFile(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetFieldValue<byte[]>(2),
                reader.GetFieldValue<byte[]>(3));
        }
    }

    private async Task<Fixture> NewFixtureAsync()
    {
        var database = await server.CreateDatabaseAsync();
        var store = database.CreateStore();
        return new Fixture(database, store, await store.GetCatalogAsync());
    }

    private static string Text(PublicFile? file) => Encoding.UTF8.GetString(Assert.IsType<PublicFile>(file).Content);

    [Fact]
    public async Task The_latest_published_version_is_served_with_its_name_and_hash()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "pdf", "v1 en pdf");

        var file = await f.GetAsync("en", "pdf");

        Assert.NotNull(file);
        Assert.Equal(1, file.VersionNumber);
        Assert.Equal("Ada Lovelace", file.Name);
        Assert.Equal("v1 en pdf", Encoding.UTF8.GetString(file.Content));
        Assert.Equal(SHA256.HashData(file.Content), file.ContentHash);
    }

    [Fact]
    public async Task Publishing_a_newer_version_changes_what_latest_serves_and_keeps_the_permalink()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "pdf", "v1");
        var v2 = await f.SaveAsync("Principal engineer", "en");
        await f.AddRenderAsync(v2.Id, "en", "pdf", "v2");

        Assert.Equal("v2", Text(await f.GetAsync("en", "pdf")));
        Assert.Equal("v1", Text(await f.GetAsync("en", "pdf", version: 1))); // what an employer received
        Assert.Equal("v2", Text(await f.GetAsync("en", "pdf", version: 2)));
    }

    [Fact]
    public async Task A_draft_is_never_served_even_with_its_number_and_files()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "pdf", "v1");
        var v2 = await f.SaveAsync("Unpublished draft"); // saved, never published
        await f.AddRenderAsync(v2.Id, "en", "pdf", "draft");

        Assert.Null(await f.GetAsync("en", "pdf", version: 2));
        Assert.Equal("v1", Text(await f.GetAsync("en", "pdf")));
    }

    [Fact]
    public async Task A_version_is_served_only_in_the_locales_it_was_published_for()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "pdf", "en");
        await f.AddRenderAsync(v1.Id, "nb", "pdf", "nb, never published");

        Assert.Null(await f.GetAsync("nb", "pdf"));
        Assert.Null(await f.GetAsync("nb", "pdf", version: 1));
    }

    [Fact]
    public async Task Unpublishing_empties_latest_but_keeps_permalinks()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en", "nb");
        await f.AddRenderAsync(v1.Id, "nb", "pdf", "nb");

        Assert.IsType<PublishResult.Published>(await f.Store.PublishAsync(new PublishRequest(null, ["nb"], Author)));

        Assert.Null(await f.GetAsync("nb", "pdf"));
        Assert.Equal("nb", Text(await f.GetAsync("nb", "pdf", version: 1)));
    }

    [Fact]
    public async Task The_newest_render_is_served_after_re_rendering()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "html", "old renderer", rendererVersion: "html/2");
        await f.AddRenderAsync(v1.Id, "en", "html", "new renderer", rendererVersion: "html/3");

        Assert.Equal("new renderer", Text(await f.GetAsync("en", "html")));
    }

    [Theory]
    [InlineData("en", "docx", null)]   // unknown format
    [InlineData("de", "pdf", null)]    // unknown locale
    [InlineData("zxx", "pdf", null)]   // never publishable
    [InlineData("en", "md", null)]     // no file in that format
    [InlineData("en", "pdf", 99)]      // no such version
    [InlineData("en", "pdf", 0)]
    [InlineData("en", "pdf", -1)]
    public async Task Anything_else_returns_nothing(string locale, string format, int? version)
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await f.AddRenderAsync(v1.Id, "en", "pdf", "v1");

        Assert.Null(await f.GetAsync(locale, format, version));
    }

    [Theory]
    [InlineData("SELECT count(*) FROM cv.cv_renders")]
    [InlineData("SELECT count(*) FROM cv.cv_versions")]
    [InlineData("SELECT count(*) FROM cv.cv_node_contents")]
    [InlineData("SELECT count(*) FROM cv.cv_published")]
    [InlineData("SELECT count(*) FROM cv.users")]
    [InlineData("SELECT * FROM cv.cv_version_localized(gen_random_uuid())")]
    [InlineData("INSERT INTO cv.audit_log (id, action) VALUES (gen_random_uuid(), 'DOWNLOAD')")]
    public async Task The_public_role_can_do_nothing_but_call_the_function(string sql)
    {
        var f = await NewFixtureAsync();
        await using var connection = new NpgsqlConnection(f.Database.PublicConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task A_stored_file_must_match_its_hash_and_not_be_empty()
    {
        var f = await NewFixtureAsync();
        var v1 = await f.SaveAsync("Engineer", "en");
        await using var connection = new NpgsqlConnection(f.Database.AppConnectionString);
        await connection.OpenAsync();

        async Task<string> Insert(byte[] hash, byte[] content)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO cv.cv_renders (version_id, locale, format, renderer_version, content_hash, content) VALUES ($1, 'en', 'pdf', 'test/1', $2, $3)",
                connection);
            command.Parameters.Add(new() { Value = v1.Id });
            command.Parameters.Add(new() { Value = hash });
            command.Parameters.Add(new() { Value = content });
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            return error.ConstraintName!;
        }

        Assert.Equal("ck_cv_renders_content_hash_matches", await Insert(SHA256.HashData("other"u8), "pdf"u8.ToArray()));
        Assert.Equal("ck_cv_renders_content_not_empty", await Insert(SHA256.HashData([]), []));
    }
}
