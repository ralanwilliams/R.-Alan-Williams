using System.Text.RegularExpressions;

namespace Cv.Core.Rendering;

/// <summary>Locale-specific typographic fixes applied to prose at render time; the stored text is left alone.</summary>
public static partial class Typography
{
    /// <summary>U+202F NARROW NO-BREAK SPACE.</summary>
    public const char NarrowNoBreakSpace = ' ';

    /// <summary>
    /// French: the space before <c>: ; ! ?</c> and <c>»</c>, and after <c>«</c>, becomes a narrow
    /// no-break space, so the mark never wraps onto its own line. Only an existing space is
    /// converted: inserting one would break times ("10:30") and the like.
    /// </summary>
    public static string Apply(string locale, string text) =>
        IsFrench(locale)
            ? SpaceAfterOpeningGuillemet().Replace(SpaceBeforeHighPunctuation().Replace(text, $"{NarrowNoBreakSpace}$1"), $"«{NarrowNoBreakSpace}")
            : text;

    private static bool IsFrench(string locale) =>
        locale == "fr" || locale.StartsWith("fr-", StringComparison.Ordinal);

    [GeneratedRegex(@"[  ]([:;!?»])")]
    private static partial Regex SpaceBeforeHighPunctuation();

    [GeneratedRegex(@"«[  ]")]
    private static partial Regex SpaceAfterOpeningGuillemet();
}
