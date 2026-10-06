using Cv.Core.Drafts;
using Cv.Core.Model;
using Cv.Core.Validation;

namespace Cv.Editor.Api;

// Request and response bodies of the editor API (JSON, camelCase). Documents travel as
// Cv.Core.Drafts.Draft in both directions: nodes in document order, texts per locale.

public sealed record SessionResponse(
    UserDto User,
    string SourceLocale,
    string NeutralLocale,
    IReadOnlyList<LocaleDto> Locales,
    IReadOnlyList<NodeTypeDto> NodeTypes,
    IReadOnlyDictionary<string, IReadOnlyList<AttributeRule>> Attributes);

public sealed record UserDto(string DisplayName, string Email);

public sealed record LocaleDto(string Code, string Name, bool IsSource, bool IsPublishable);

public sealed record NodeTypeDto(string Code, string Description, bool HasText, IReadOnlyList<string> Children);

public sealed record VersionDto(
    Guid Id,
    int Number,
    string? Summary,
    DateTimeOffset CreatedAt,
    int? RestoredFromNumber,
    IReadOnlyList<string> PublishedLocales);

public sealed record PublishedDto(string Locale, Guid VersionId, int VersionNumber, DateTimeOffset PublishedAt);

/// <summary>
/// A document to edit. <see cref="Version"/> is null before the first save; the document is then
/// the seed file named in <see cref="SeedFile"/>, or the starter document.
/// </summary>
public sealed record DocumentResponse(VersionDto? Version, Draft Document, string? SeedFile = null);

public sealed record HistoryResponse(IReadOnlyList<VersionDto> Versions, IReadOnlyList<PublishedDto> Published);

public sealed record AnalyzeRequest(Guid? BaseVersionId, Draft Document, string Locale);

/// <param name="NodeStatus">Per node id, per locale: "missing", "stale" or "hidden". Nodes that are fine are absent.</param>
public sealed record AnalyzeResponse(
    string Html,
    bool HasChanges,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<ReadinessDto> Readiness,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> NodeStatus);

public sealed record ReadinessDto(string Locale, int Missing, int Stale, bool CanPublish);

public sealed record SaveRequest(
    Guid? BaseVersionId,
    Draft Document,
    string? Summary = null,
    IReadOnlyList<string>? Publish = null,
    bool ConfirmStale = false);

public sealed record SaveResponse(VersionDto Version, IReadOnlyList<PublishedDto> Published);

public sealed record RestoreRequest(Guid? BaseVersionId);

/// <param name="VersionId">The version to publish, or null to unpublish the locales.</param>
public sealed record PublishRequestDto(Guid? VersionId, IReadOnlyList<string> Locales, bool ConfirmStale = false);

public sealed record PdfRequest(Guid? BaseVersionId, Draft Document, string Locale);

/// <param name="Label">The line's text (source locale, else language-neutral) so a change is recognisable.</param>
public sealed record ChangeDto(Guid NodeId, string? NodeType, string? Label, string? Locale, string Change, string? OldValue, string? NewValue);
