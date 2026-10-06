using System.Collections.Concurrent;
using System.Text.Json;

namespace Cv.Core.Rendering;

/// <summary>
/// The renderer's per-locale strings, loaded from <c>Rendering/Messages/{locale}.json</c>
/// (embedded in the assembly). Adding a locale means adding a file; a test checks that every
/// file has every key.
/// </summary>
public sealed record Messages(
    IReadOnlyList<string> Months,
    string Present,
    string DateRange,
    string DocumentTitle,
    string Missing,
    string Label)
{
    private static readonly ConcurrentDictionary<string, Messages?> Cache = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The messages for <paramref name="locale"/>, or null if there is no message file for it.</summary>
    public static Messages? Find(string locale) => Cache.GetOrAdd(locale, Load);

    public static Messages For(string locale) =>
        Find(locale) ?? throw new InvalidOperationException(
            $"No renderer messages for locale '{locale}'. Add Rendering/Messages/{locale}.json to Cv.Core.");

    /// <summary>"Mar 2021" from "2021-03"; a bare year stays as it is.</summary>
    public string FormatDate(string yearMonth) =>
        yearMonth.Length == 7 && int.TryParse(yearMonth.AsSpan(5, 2), out var month) && month is >= 1 and <= 12
            ? $"{Months[month - 1]} {yearMonth[..4]}"
            : yearMonth;

    /// <summary>"Mar 2021 – Present", "2019 – 2021", or null when there are no dates.</summary>
    public string? FormatRange(string? start, string? end) => (start, end) switch
    {
        (null, null) => null,
        (null, { } only) => FormatDate(only),
        ({ } from, var to) => DateRange
            .Replace("{start}", FormatDate(from))
            .Replace("{end}", to is null ? Present : FormatDate(to)),
    };

    private static Messages? Load(string locale)
    {
        using var stream = typeof(Messages).Assembly.GetManifestResourceStream($"Cv.Core.Rendering.Messages.{locale}.json");
        if (stream is null)
        {
            return null;
        }

        var messages = JsonSerializer.Deserialize<Messages>(stream, Json)
            ?? throw new InvalidDataException($"Rendering/Messages/{locale}.json is empty.");
        if (messages.Months is not { Count: 12 } || new[] { messages.Present, messages.DateRange, messages.DocumentTitle, messages.Missing, messages.Label }.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"Rendering/Messages/{locale}.json must define 12 months and every message.");
        }
        return messages;
    }
}
