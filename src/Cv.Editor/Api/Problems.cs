using Cv.Core.Localization;
using Cv.Core.Model;
using Cv.Core.Validation;

namespace Cv.Editor.Api;

/// <summary>
/// RFC 7807 problem responses. Each carries a stable <c>code</c> extension that the page
/// branches on; titles and details are for people.
/// </summary>
internal static class Problems
{
    /// <summary>The draft's base is not the latest version: reload instead of retrying (ADR 0001 §3).</summary>
    public static IResult Conflict(VersionDto? latest) => Problem(
        StatusCodes.Status409Conflict, "conflict", "Saved somewhere else",
        latest is null
            ? "The version this draft started from is no longer the latest. Reload to continue from the current version."
            : $"v{latest.Number} was saved in another tab or window after this draft was opened. Reload to continue from it.",
        ("latest", latest));

    public static IResult Invalid(IReadOnlyList<ValidationIssue> issues) => Problem(
        StatusCodes.Status422UnprocessableEntity, "invalid", "The CV has problems",
        issues.Count == 1 ? issues[0].Message : $"{issues.Count} problems need fixing first.",
        ("issues", issues));

    public static IResult NoChanges(string detail) => Problem(
        StatusCodes.Status422UnprocessableEntity, "no-changes", "Nothing to save", detail);

    public static IResult Rejected(string detail) => Problem(
        StatusCodes.Status422UnprocessableEntity, "rejected", "The database refused the change", detail);

    /// <summary>
    /// Null when every locale can be published. Missing text blocks publishing outright, exactly
    /// as the database trigger does; stale translations need the author's explicit confirmation
    /// (ADR 0001 §6).
    /// </summary>
    public static IResult? CheckPublishable(CvDocument document, CvCatalog catalog, IReadOnlyList<string> locales, bool confirmStale)
    {
        if (locales.Count == 0)
        {
            return null;
        }
        if (locales.FirstOrDefault(l => catalog.FindLocale(l) is not { IsPublishable: true }) is { } unknown)
        {
            return Invalid([new ValidationIssue("locale", $"'{unknown}' cannot be published.")]);
        }

        var readiness = locales.Select(l => Localizer.Localize(document, catalog, l).Readiness).ToList();
        var dtos = readiness.Select(EditorApi.ToDto).ToList();

        var blocked = readiness.Where(r => !r.CanPublish).ToList();
        if (blocked.Count > 0)
        {
            return Problem(
                StatusCodes.Status422UnprocessableEntity, "not-ready", "Some languages have missing text",
                string.Join("; ", blocked.Select(r => $"{Name(catalog, r.Locale)}: {Lines(r.MissingCount)} without text")) +
                ". Write them or leave them out of that language.",
                ("readiness", dtos));
        }

        var stale = readiness.Where(r => r.StaleCount > 0).ToList();
        if (stale.Count > 0 && !confirmStale)
        {
            return Problem(
                StatusCodes.Status422UnprocessableEntity, "confirm-stale", "Some translations may be out of date",
                string.Join("; ", stale.Select(r => $"{Name(catalog, r.Locale)}: {Lines(r.StaleCount)}")) +
                " translated from English text that has changed since. Publish anyway?",
                ("readiness", dtos));
        }

        return null;
    }

    private static string Name(CvCatalog catalog, string locale) => catalog.FindLocale(locale)?.Name ?? locale;

    private static string Lines(int count) => count == 1 ? "1 line" : $"{count} lines";

    private static IResult Problem(int status, string code, string title, string detail, params (string Key, object? Value)[] extensions)
    {
        var all = new Dictionary<string, object?> { ["code"] = code };
        foreach (var (key, value) in extensions)
        {
            all[key] = value;
        }
        return Results.Problem(statusCode: status, title: title, detail: detail, extensions: all);
    }
}
