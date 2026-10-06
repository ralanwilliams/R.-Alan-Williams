using System.Text.RegularExpressions;

namespace Cv.Core.Model;

/// <summary>How the editor should offer an attribute.</summary>
public enum AttributeKind
{
    /// <summary><c>YYYY</c> or <c>YYYY-MM</c>.</summary>
    YearMonth,

    /// <summary>One of <see cref="AttributeRule.Options"/>.</summary>
    Choice,
}

public sealed record AttributeRule(string Key, AttributeKind Kind, IReadOnlyList<string> Options);

/// <summary>
/// The language-neutral attributes (<c>cv_nodes.attrs</c>) each node type may carry.
/// </summary>
/// <remarks>
/// The database only requires <c>attrs</c> to be a JSON object; this application is stricter:
/// attrs are a flat map of string values, and only the keys below are allowed. Dates are
/// stored language-neutrally and formatted per locale by the renderer (ADR 0001 §5).
/// </remarks>
public static partial class NodeAttributes
{
    public const string Start = "start";
    public const string End = "end";
    public const string Kind = "kind";

    public static class ContactKinds
    {
        public const string Email = "email";
        public const string Phone = "phone";
        public const string Url = "url";
        public const string Location = "location";
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<AttributeRule>> Rules { get; } =
        new Dictionary<string, IReadOnlyList<AttributeRule>>(StringComparer.Ordinal)
        {
            ["entry"] =
            [
                new(Start, AttributeKind.YearMonth, []),
                new(End, AttributeKind.YearMonth, []),
            ],
            ["contact"] =
            [
                new(Kind, AttributeKind.Choice, [ContactKinds.Email, ContactKinds.Phone, ContactKinds.Url, ContactKinds.Location]),
            ],
        };

    /// <summary>Problems with <paramref name="attrs"/> for a node of <paramref name="type"/>; empty when valid.</summary>
    public static IEnumerable<string> Validate(string type, IReadOnlyDictionary<string, string> attrs)
    {
        var rules = Rules.GetValueOrDefault(type) ?? [];
        foreach (var (key, value) in attrs)
        {
            var rule = rules.FirstOrDefault(r => r.Key == key);
            if (rule is null)
            {
                yield return rules.Count == 0
                    ? $"A {type} has no attributes, but '{key}' was given."
                    : $"Unknown attribute '{key}' for a {type}. Allowed: {string.Join(", ", rules.Select(r => r.Key))}.";
                continue;
            }

            switch (rule.Kind)
            {
                case AttributeKind.YearMonth when !YearMonthPattern().IsMatch(value):
                    yield return $"'{key}' must be a year (2021) or a year and month (2021-03), got '{value}'.";
                    break;
                case AttributeKind.Choice when !rule.Options.Contains(value, StringComparer.Ordinal):
                    yield return $"'{key}' must be one of {string.Join(", ", rule.Options)}, got '{value}'.";
                    break;
            }
        }

        if (attrs.TryGetValue(Start, out var start) && attrs.TryGetValue(End, out var end)
            && YearMonthPattern().IsMatch(start) && YearMonthPattern().IsMatch(end)
            && EndsBefore(end, start))
        {
            yield return $"The end date ({end}) is before the start date ({start}).";
        }
    }

    /// <summary>
    /// YYYY and YYYY-MM strings sort chronologically. Compare at the precision both have, so
    /// "2021" against "2021-03" counts as the same year, not as out of order.
    /// </summary>
    private static bool EndsBefore(string end, string start)
    {
        var length = Math.Min(start.Length, end.Length);
        return string.CompareOrdinal(end[..length], start[..length]) < 0;
    }

    [GeneratedRegex(@"^\d{4}(-(0[1-9]|1[0-2]))?$")]
    private static partial Regex YearMonthPattern();
}
