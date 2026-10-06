using Cv.Core.Drafts;
using Cv.Core.Hashing;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Data.Store;
using Cv.Testing;
using Npgsql;

namespace Cv.Data.Tests;

/// <summary>The store against the real schema: what the app does, and what the database lets it do.</summary>
public sealed class CvStoreTests(PostgresServer server)
{
    private static readonly Guid Author = PostgresServer.UserId;

    private async Task<(CvStore Store, CvCatalog Catalog, TestDatabase Database)> NewStoreAsync()
    {
        var database = await server.CreateDatabaseAsync();
        var store = database.CreateStore();
        return (store, await store.GetCatalogAsync(), database);
    }

    private static CvDocument Build(Draft draft, CvDocument? baseDocument, CvCatalog catalog)
    {
        var result = DraftBuilder.Build(draft, baseDocument, catalog);
        Assert.Empty(result.Issues);
        return result.Document;
    }

    /// <summary>Loads a version the way the editor does, applies a text change, and builds it against that version.</summary>
    private static CvDocument EditText(CvDocument saved, CvCatalog catalog, Guid node, string locale, string content)
    {
        var draft = Draft.FromDocument(saved);
        return Build(draft with
        {
            Texts = draft.Texts.Select(t => t.NodeId == node && t.Locale == locale ? t with { Content = content } : t).ToList(),
        }, saved, catalog);
    }

    private static VersionInfo Saved(SaveResult result) => Assert.IsType<SaveResult.Saved>(result).Version;

    [Fact]
    public async Task The_database_catalog_matches_the_one_the_unit_tests_use()
    {
        var (_, catalog, _) = await NewStoreAsync();
        var expected = TestDocuments.Catalog;

        Assert.Equal(expected.Locales, catalog.Locales);
        Assert.Equal(expected.NodeTypes.Select(t => (t.Code, t.HasText)), catalog.NodeTypes.Select(t => (t.Code, t.HasText)));
        Assert.All(catalog.NodeTypes, t => Assert.Equal(expected.AllowedChildren(t.Code), catalog.AllowedChildren(t.Code)));
    }

    [Fact]
    public async Task A_saved_version_loads_back_identically()
    {
        var (store, catalog, database) = await NewStoreAsync();
        var document = Build(TestDraft.Sample().Draft.ToDraft(), null, catalog);

        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, document, Author, "First version")));
        var loaded = await database.CreateStore().LoadDocumentAsync(v1.Id); // fresh store: really from the database

        Assert.Equal(1, v1.Number);
        Assert.Equal(ContentHash.Compute(document), v1.ContentHash);
        Assert.Equal(v1.ContentHash, ContentHash.Compute(loaded));
        Assert.Equal(v1.ContentHash, ContentHash.Compute(Build(Draft.FromDocument(loaded), loaded, catalog)));
    }

    [Fact]
    public async Task A_save_based_on_a_superseded_version_is_a_conflict()
    {
        // Two tabs open on v1 (ADR 0001 §3): the first save wins, the second must reload.
        var (store, catalog, _) = await NewStoreAsync();
        var (sample, ids) = TestDraft.Sample();
        var v1Document = Build(sample.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, v1Document, Author)));

        var firstTab = await store.SaveVersionAsync(new NewVersion(v1.Id, EditText(v1Document, catalog, ids.Headline, "en", "Principal engineer"), Author));
        var secondTab = await store.SaveVersionAsync(new NewVersion(v1.Id, EditText(v1Document, catalog, ids.Bullet, "en", "Designed the engine"), Author));

        Assert.Equal(2, Saved(firstTab).Number);
        var conflict = Assert.IsType<SaveResult.Conflict>(secondTab);
        Assert.Equal(2, conflict.Latest?.Number);
        Assert.Equal(2, (await store.ListVersionsAsync()).Count);
    }

    [Fact]
    public async Task Saving_identical_content_reports_no_changes()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var document = Build(TestDraft.Sample().Draft.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, document, Author)));

        var again = await store.SaveVersionAsync(new NewVersion(v1.Id, Build(Draft.FromDocument(document), document, catalog), Author));

        Assert.IsType<SaveResult.NoChanges>(again);
    }

    [Fact]
    public async Task Save_and_publish_is_all_or_nothing()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var d = new TestDraft();
        d.Add(d.Root, "section", en: "Experience", nb: "Erfaring"); // no French
        var document = Build(d.ToDraft(), null, catalog);

        var result = await store.SaveVersionAsync(new NewVersion(null, document, Author, PublishLocales: ["en", "fr"]));

        var rejected = Assert.IsType<SaveResult.Rejected>(result);
        Assert.Contains("Cannot publish locale fr", rejected.Message);
        Assert.Empty(await store.ListVersionsAsync());   // the version was rolled back too
        Assert.Empty(await store.GetPublishedAsync());
    }

    [Fact]
    public async Task Publishing_is_per_locale_and_reverting_is_publishing_an_older_version()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var (sample, ids) = TestDraft.Sample();
        var v1Document = Build(sample.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, v1Document, Author, PublishLocales: ["en", "nb"])));
        var v2 = Saved(await store.SaveVersionAsync(new NewVersion(v1.Id, EditText(v1Document, catalog, ids.Headline, "en", "Principal engineer"), Author, PublishLocales: ["en"])));

        async Task<Dictionary<string, Guid>> Live() => (await store.GetPublishedAsync()).ToDictionary(p => p.Locale, p => p.VersionId);

        Assert.Equal(new Dictionary<string, Guid> { ["en"] = v2.Id, ["nb"] = v1.Id }, await Live());

        Assert.IsType<PublishResult.Published>(await store.PublishAsync(new PublishRequest(v1.Id, ["en"], Author)));
        Assert.Equal(v1.Id, (await Live())["en"]);

        Assert.IsType<PublishResult.Published>(await store.PublishAsync(new PublishRequest(null, ["nb"], Author)));
        Assert.False((await Live()).ContainsKey("nb"));
    }

    [Fact]
    public async Task The_language_neutral_locale_cannot_be_published()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, Build(TestDraft.Sample().Draft.ToDraft(), null, catalog), Author)));

        var result = await store.PublishAsync(new PublishRequest(v1.Id, ["zxx"], Author));

        Assert.Contains("Only these languages can be published", Assert.IsType<PublishResult.Rejected>(result).Message);
    }

    [Fact]
    public async Task Restoring_appends_a_verbatim_copy()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var (sample, ids) = TestDraft.Sample();
        var v1Document = Build(sample.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, v1Document, Author)));
        var v2 = Saved(await store.SaveVersionAsync(new NewVersion(v1.Id, EditText(v1Document, catalog, ids.Bullet, "en", "Rewritten"), Author)));

        var v3 = Saved(await store.SaveVersionAsync(new NewVersion(v2.Id, await store.LoadDocumentAsync(v1.Id), Author, "Restored from v1", RestoredFromVersionId: v1.Id)));

        Assert.Equal(3, v3.Number);
        Assert.Equal(v1.Id, v3.RestoredFromVersionId);
        Assert.Equal(v1.ContentHash, v3.ContentHash);
    }

    [Fact]
    public async Task The_diff_reports_an_edited_line()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var (sample, ids) = TestDraft.Sample();
        var v1Document = Build(sample.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, v1Document, Author)));
        var v2 = Saved(await store.SaveVersionAsync(new NewVersion(v1.Id, EditText(v1Document, catalog, ids.Bullet, "en", "Designed the engine"), Author)));

        var changes = await store.DiffAsync(v1.Id, v2.Id);

        var change = Assert.Single(changes);
        Assert.Equal((ids.Bullet, "en", "text_edited", "Built the difference engine", "Designed the engine"),
            (change.NodeId, change.Locale, change.Change, change.OldValue, change.NewValue));
    }

    [Fact]
    public async Task The_database_enforces_the_grammar_even_if_the_application_does_not()
    {
        var (store, catalog, _) = await NewStoreAsync();
        var valid = Build(TestDraft.Sample().Draft.ToDraft(), null, catalog);
        var bulletUnderRoot = new DocumentNode(Guid.CreateVersion7(), valid.Root!.Id, "bullet", "b00", DocumentNode.NoAttrs);
        var invalid = new CvDocument([.. valid.Nodes, bulletUnderRoot], valid.Texts); // bypasses DraftBuilder's validation

        var result = await store.SaveVersionAsync(new NewVersion(null, invalid, Author));

        Assert.Equal("A root node cannot contain a bullet node", Assert.IsType<SaveResult.Rejected>(result).Message);
    }

    [Fact]
    public async Task The_localizer_agrees_with_cv_version_localized()
    {
        // The editor computes missing/stale/hidden in C# for unsaved drafts; publishing relies on
        // the SQL function. They must agree on every flag, every text and the document order.
        var (store, catalog, database) = await NewStoreAsync();
        var (sample, ids) = TestDraft.Sample();
        var education = sample.Add(sample.Root, "section", en: "Education", nb: "Utdanning", fr: "Formation");
        var degree = sample.Add(education, "entry", en: "MSc Mathematics", fr: "Master de mathématiques"); // no nb: missing
        sample.Add(degree, "bullet", en: "Thesis on engines");                                            // only en
        sample.Omit(education, "fr");                                                                      // hides the subtree in fr
        sample.Add(sample.Root, "contact", zxx: "London", fr: "Londres", attrs: new Dictionary<string, string> { ["kind"] = "location" });

        var v1Document = Build(sample.ToDraft(), null, catalog);
        var v1 = Saved(await store.SaveVersionAsync(new NewVersion(null, v1Document, Author)));
        // Change English text so the untouched translations become stale.
        var v2Document = EditText(v1Document, catalog, ids.Bullet, "en", "Designed and built the difference engine");
        var v2 = Saved(await store.SaveVersionAsync(new NewVersion(v1.Id, v2Document, Author)));

        var expected = Localizer.LocalizeAll(v2Document, catalog)
            .OrderBy(d => d.Locale.Code, StringComparer.Ordinal)
            .SelectMany(d => d.Nodes.Select(n => (d.Locale.Code, n.Node.Id, n.Depth, n.Content, n.IsHidden, n.IsMissing, n.IsStale)))
            .ToList();

        var actual = new List<(string, Guid, int, string?, bool, bool, bool)>();
        await using (var connection = new NpgsqlConnection(database.AppConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT locale, node_id, depth, content, is_omitted, is_missing, is_stale
                FROM cv.cv_version_localized(@version)
                ORDER BY locale COLLATE "C", sort_path COLLATE "C"
                """, connection);
            command.Parameters.AddWithValue("version", v2.Id);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actual.Add((reader.GetString(0), reader.GetGuid(1), reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6)));
            }
        }

        Assert.Equal(expected, actual);
        // And the scenario really exercises every flag:
        Assert.Contains(actual, r => r.Item6);  // missing
        Assert.Contains(actual, r => r.Item7);  // stale
        Assert.Contains(actual, r => r.Item5);  // hidden
    }
}
