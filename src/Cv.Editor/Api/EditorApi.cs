using Cv.Core.Drafts;
using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Core.Rendering;
using Cv.Core.Validation;
using Cv.Data.Store;
using Cv.Editor.Pdf;

namespace Cv.Editor.Api;

/// <summary>
/// The HTTP API behind the editor page. Handlers stay thin: Cv.Core decides what a document
/// means, the database decides what may be stored, and this layer maps their answers to HTTP.
/// </summary>
internal static class EditorApi
{
    public static IEndpointRouteBuilder MapEditorApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/session", GetSession);
        api.MapGet("/document", GetCurrentDocument);
        api.MapGet("/versions", GetHistory);
        api.MapGet("/versions/{id:guid}", GetVersion);
        api.MapGet("/versions/{from:guid}/changes/{to:guid}", GetChanges);
        api.MapPost("/analyze", Analyze);
        api.MapPost("/versions", Save);
        api.MapPost("/versions/{id:guid}/restore", Restore);
        api.MapPost("/publications", Publish);
        api.MapPost("/pdf", DownloadPdf);

        // The preview links its stylesheet instead of inlining it, so the CSP can forbid inline styles.
        app.MapGet("/render/cv.css", () => Results.Text(HtmlRenderer.Css, "text/css; charset=utf-8"));
        return app;
    }

    private static async Task<SessionResponse> GetSession(ICvStore store, EditorWorkspace workspace, CancellationToken ct)
    {
        var user = await workspace.GetUserAsync(ct);
        var catalog = await store.GetCatalogAsync(ct);
        return new SessionResponse(
            new UserDto(user.DisplayName, user.Email),
            catalog.Source.Code,
            CvCatalog.NeutralLocale,
            catalog.Locales.Select(l => new LocaleDto(l.Code, l.Name, l.IsSource, l.IsPublishable)).ToList(),
            catalog.NodeTypes.Select(t => new NodeTypeDto(t.Code, t.Description, t.HasText, catalog.AllowedChildren(t.Code))).ToList(),
            NodeAttributes.Rules);
    }

    private static async Task<DocumentResponse> GetCurrentDocument(ICvStore store, EditorWorkspace workspace, CancellationToken ct)
    {
        if (await store.GetLatestVersionAsync(ct) is { } latest)
        {
            var history = await History.LoadAsync(store, ct);
            return new DocumentResponse(history.Describe(latest), Draft.FromDocument(await store.LoadDocumentAsync(latest.Id, ct)));
        }

        // No version yet: open the seed file if one is configured (read on every request, so
        // edits to it show up on reload), else the starter document. Nothing is saved here.
        if (await workspace.LoadSeedAsync(ct) is { } seed)
        {
            return new DocumentResponse(null, seed.Draft, seed.FileName);
        }

        var user = await workspace.GetUserAsync(ct);
        return new DocumentResponse(null, StarterDraft.Create(user.DisplayName, user.Email, await store.GetCatalogAsync(ct)));
    }

    private static async Task<HistoryResponse> GetHistory(ICvStore store, CancellationToken ct)
    {
        var history = await History.LoadAsync(store, ct);
        return new HistoryResponse(history.Versions.Select(history.Describe).ToList(), history.PublishedDtos());
    }

    private static async Task<IResult> GetVersion(Guid id, ICvStore store, CancellationToken ct)
    {
        var history = await History.LoadAsync(store, ct);
        return history.Find(id) is { } version
            ? Results.Ok(new DocumentResponse(history.Describe(version), Draft.FromDocument(await store.LoadDocumentAsync(id, ct))))
            : Results.NotFound();
    }

    private static async Task<IResult> GetChanges(Guid from, Guid to, ICvStore store, CancellationToken ct)
    {
        if (await store.GetVersionAsync(from, ct) is null || await store.GetVersionAsync(to, ct) is null)
        {
            return Results.NotFound();
        }

        var catalog = await store.GetCatalogAsync(ct);
        var before = await store.LoadDocumentAsync(from, ct);
        var after = await store.LoadDocumentAsync(to, ct);
        var changes = await store.DiffAsync(from, to, ct);

        string? Label(Guid node) => new[] { after, before }
            .SelectMany(d => new[] { d.TextOf(node, catalog.Source.Code), d.TextOf(node, CvCatalog.NeutralLocale) })
            .FirstOrDefault(t => t?.Content is not null)?.Content;

        return Results.Ok(changes
            .Select(c => new ChangeDto(c.NodeId, (after.Find(c.NodeId) ?? before.Find(c.NodeId))?.Type, Label(c.NodeId), c.Locale, c.Change, c.OldValue, c.NewValue))
            .ToList());
    }

    private static async Task<IResult> Analyze(AnalyzeRequest request, EditorWorkspace workspace, CancellationToken ct)
    {
        var prepared = await PrepareOrConflictAsync(workspace, request.BaseVersionId, request.Document, ct);
        if (prepared.Problem is { } problem)
        {
            return problem;
        }
        var draft = prepared.Draft!;
        if (draft.Catalog.FindLocale(request.Locale) is not { IsPublishable: true })
        {
            return Problems.Invalid([new ValidationIssue("locale", $"'{request.Locale}' is not a language the CV is published in.")]);
        }

        var localized = Localizer.LocalizeAll(draft.Document, draft.Catalog);
        var status = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var document in localized)
        {
            foreach (var node in document.Nodes)
            {
                var flag = node.IsMissing ? "missing" : node.IsStale ? "stale" : node.IsHidden ? "hidden" : null;
                if (flag is null)
                {
                    continue;
                }
                if (!status.TryGetValue(node.Node.Id, out var perLocale))
                {
                    status[node.Node.Id] = perLocale = [];
                }
                perLocale[document.Locale.Code] = flag;
            }
        }

        var html = HtmlRenderer.Render(
            localized.Single(d => d.Locale.Code == request.Locale),
            new RenderOptions(Preview: true, StylesheetHref: "/render/cv.css"));

        return Results.Ok(new AnalyzeResponse(
            html,
            draft.HasChanges,
            draft.Build.Issues,
            localized.Select(d => ToDto(d.Readiness)).ToList(),
            status.ToDictionary(s => s.Key, s => (IReadOnlyDictionary<string, string>)s.Value)));
    }

    private static async Task<IResult> Save(SaveRequest request, EditorWorkspace workspace, ICvStore store, CancellationToken ct)
    {
        var prepared = await PrepareOrConflictAsync(workspace, request.BaseVersionId, request.Document, ct);
        if (prepared.Problem is { } problem)
        {
            return problem;
        }
        var draft = prepared.Draft!;
        if (!draft.Build.IsValid)
        {
            return Problems.Invalid(draft.Build.Issues);
        }

        var publish = (request.Publish ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (Problems.CheckPublishable(draft.Document, draft.Catalog, publish, request.ConfirmStale) is { } notPublishable)
        {
            return notPublishable;
        }

        // "Save & publish" with nothing new to save publishes the version the draft is based on.
        if (!draft.HasChanges && draft.Base is { } unchanged)
        {
            return publish.Count == 0
                ? Problems.NoChanges($"This is the same as v{unchanged.Info.Number}.")
                : await PublishAndDescribeAsync(store, workspace, unchanged.Info.Id, publish, ct);
        }

        var user = await workspace.GetUserAsync(ct);
        var result = await store.SaveVersionAsync(
            new NewVersion(request.BaseVersionId, draft.Document, user.Id, request.Summary, PublishLocales: publish), ct);
        return await ToResultAsync(result, store, ct);
    }

    private static async Task<IResult> Restore(Guid id, RestoreRequest request, EditorWorkspace workspace, ICvStore store, CancellationToken ct)
    {
        if (await store.GetVersionAsync(id, ct) is not { } target)
        {
            return Results.NotFound();
        }
        if (request.BaseVersionId is null)
        {
            return Problems.Invalid([new ValidationIssue("base", "Restoring needs the version you are looking at as its base.")]);
        }

        var user = await workspace.GetUserAsync(ct);
        var result = await store.SaveVersionAsync(
            new NewVersion(request.BaseVersionId, await store.LoadDocumentAsync(id, ct), user.Id, $"Restored from v{target.Number}", RestoredFromVersionId: id),
            ct);
        return result is SaveResult.NoChanges
            ? Problems.NoChanges($"v{target.Number} has the same content as the current version.")
            : await ToResultAsync(result, store, ct);
    }

    private static async Task<IResult> Publish(PublishRequestDto request, EditorWorkspace workspace, ICvStore store, CancellationToken ct)
    {
        var catalog = await store.GetCatalogAsync(ct);
        var locales = request.Locales.Distinct(StringComparer.Ordinal).ToList();
        if (locales.Count == 0 || locales.Any(l => catalog.FindLocale(l) is not { IsPublishable: true }))
        {
            return Problems.Invalid([new ValidationIssue("locale", "Choose one or more of the CV's languages.")]);
        }

        if (request.VersionId is { } versionId)
        {
            if (await store.GetVersionAsync(versionId, ct) is null)
            {
                return Results.NotFound();
            }
            var document = await store.LoadDocumentAsync(versionId, ct);
            if (Problems.CheckPublishable(document, catalog, locales, request.ConfirmStale) is { } notPublishable)
            {
                return notPublishable;
            }
        }

        return await PublishAndDescribeAsync(store, workspace, request.VersionId, locales, ct);
    }

    private static async Task<IResult> DownloadPdf(PdfRequest request, EditorWorkspace workspace, IPdfRenderer pdf, CancellationToken ct)
    {
        var prepared = await PrepareOrConflictAsync(workspace, request.BaseVersionId, request.Document, ct);
        if (prepared.Problem is { } problem)
        {
            return problem;
        }
        var draft = prepared.Draft!;
        if (draft.Catalog.FindLocale(request.Locale) is not { IsPublishable: true })
        {
            return Problems.Invalid([new ValidationIssue("locale", $"'{request.Locale}' is not a language the CV is published in.")]);
        }

        var localized = Localizer.Localize(draft.Document, draft.Catalog, request.Locale);
        var bytes = await pdf.RenderAsync(HtmlRenderer.Render(localized), ct);

        var name = localized.Nodes.FirstOrDefault(n => n.Node.Type == "name")?.Content ?? "CV";
        // Results.File sets Content-Disposition with an ASCII filename and an RFC 5987 filename* for non-ASCII names.
        return Results.File(bytes, "application/pdf", $"{name} – CV ({request.Locale}).pdf");
    }

    private static async Task<(PreparedDraft? Draft, IResult? Problem)> PrepareOrConflictAsync(
        EditorWorkspace workspace, Guid? baseVersionId, Draft document, CancellationToken ct)
    {
        try
        {
            return (await workspace.PrepareAsync(baseVersionId, document, ct), null);
        }
        catch (UnknownVersionException)
        {
            return (null, Problems.Conflict(null));
        }
    }

    private static async Task<IResult> PublishAndDescribeAsync(
        ICvStore store, EditorWorkspace workspace, Guid? versionId, IReadOnlyList<string> locales, CancellationToken ct)
    {
        var user = await workspace.GetUserAsync(ct);
        var result = await store.PublishAsync(new PublishRequest(versionId, locales, user.Id), ct);
        if (result is PublishResult.Rejected rejected)
        {
            return Problems.Rejected(rejected.Message);
        }

        var history = await History.LoadAsync(store, ct);
        return Results.Ok(new HistoryResponse(history.Versions.Select(history.Describe).ToList(), history.PublishedDtos()));
    }

    private static async Task<IResult> ToResultAsync(SaveResult result, ICvStore store, CancellationToken ct)
    {
        switch (result)
        {
            case SaveResult.Saved saved:
                var history = await History.LoadAsync(store, ct);
                return Results.Created($"/api/versions/{saved.Version.Id}", new SaveResponse(history.Describe(saved.Version), history.PublishedDtos()));
            case SaveResult.Conflict conflict:
                return Problems.Conflict(conflict.Latest is null ? null : (await History.LoadAsync(store, ct)).Describe(conflict.Latest));
            case SaveResult.NoChanges:
                return Problems.NoChanges("Nothing has changed since the version this draft is based on.");
            case SaveResult.Rejected rejected:
                return Problems.Rejected(rejected.Message);
            default:
                throw new InvalidOperationException($"Unexpected save result {result.GetType().Name}.");
        }
    }

    internal static ReadinessDto ToDto(LocaleReadiness r) => new(r.Locale, r.MissingCount, r.StaleCount, r.CanPublish);

    /// <summary>Versions and what is published, loaded together so a version can be described with both.</summary>
    private sealed class History
    {
        private readonly Dictionary<Guid, VersionInfo> _byId;

        private History(IReadOnlyList<VersionInfo> versions, IReadOnlyList<PublishedLocale> published)
        {
            Versions = versions;
            Published = published;
            _byId = versions.ToDictionary(v => v.Id);
        }

        public IReadOnlyList<VersionInfo> Versions { get; }
        public IReadOnlyList<PublishedLocale> Published { get; }

        public static async Task<History> LoadAsync(ICvStore store, CancellationToken ct) =>
            new(await store.ListVersionsAsync(ct), await store.GetPublishedAsync(ct));

        public VersionInfo? Find(Guid id) => _byId.GetValueOrDefault(id);

        public VersionDto Describe(VersionInfo v) => new(
            v.Id,
            v.Number,
            v.Summary,
            v.CreatedAt,
            v.RestoredFromVersionId is { } from ? Find(from)?.Number : null,
            Published.Where(p => p.VersionId == v.Id).Select(p => p.Locale).ToList());

        public IReadOnlyList<PublishedDto> PublishedDtos() => Published
            .Select(p => new PublishedDto(p.Locale, p.VersionId, Find(p.VersionId)?.Number ?? 0, p.PublishedAt))
            .ToList();
    }
}
