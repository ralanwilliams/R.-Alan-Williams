namespace Cv.Core.Ordering;

/// <summary>
/// Base62 fractional indexing (ADR 0001 §8): string keys that sort by ordinal comparison
/// (PostgreSQL's "C" collation), where a key can always be generated between any two others.
/// </summary>
/// <remarks>
/// A port of the algorithm in David Greenspan's "Implementing Fractional Indexing" as
/// implemented by the <c>fractional-indexing</c> npm package (rocicorp, CC0). A key is an
/// "integer part" whose first character encodes its length (<c>a0</c>, <c>a1</c>... <c>az</c>,
/// <c>b00</c>...; <c>Z</c> and below for negatives) followed by an optional fraction with no
/// trailing zero. Appending keeps working forever; inserting between two keys lengthens them.
/// Example: between <c>a0</c> and <c>a1</c> is <c>a0V</c>.
/// </remarks>
public static class FractionalIndex
{
    /// <summary>The base62 digits in ascending ordinal order: 0-9, A-Z, a-z.</summary>
    public const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private const char Zero = '0';
    private static readonly string SmallestInteger = "A" + new string(Zero, 26);

    /// <summary>A key strictly between <paramref name="before"/> and <paramref name="after"/> (either may be null for "no bound").</summary>
    public static string KeyBetween(string? before, string? after)
    {
        if (before is not null)
        {
            Validate(before);
        }
        if (after is not null)
        {
            Validate(after);
        }
        if (before is not null && after is not null && Compare(before, after) >= 0)
        {
            throw new ArgumentException($"'{before}' must sort before '{after}'.");
        }

        if (before is null)
        {
            if (after is null)
            {
                return "a" + Zero;
            }

            var integerAfter = IntegerPart(after);
            var fractionAfter = after[integerAfter.Length..];
            if (integerAfter == SmallestInteger)
            {
                return integerAfter + Midpoint("", fractionAfter);
            }
            if (Compare(integerAfter, after) < 0)
            {
                return integerAfter;
            }
            return Decrement(integerAfter) ?? throw new InvalidOperationException("Cannot generate a key before the smallest key.");
        }

        if (after is null)
        {
            var integerBefore = IntegerPart(before);
            var fractionBefore = before[integerBefore.Length..];
            return Increment(integerBefore) ?? integerBefore + Midpoint(fractionBefore, null);
        }

        var ia = IntegerPart(before);
        var fa = before[ia.Length..];
        var ib = IntegerPart(after);
        var fb = after[ib.Length..];
        if (ia == ib)
        {
            return ia + Midpoint(fa, fb);
        }

        var next = Increment(ia) ?? throw new InvalidOperationException("Cannot generate a key after the largest key.");
        return Compare(next, after) < 0 ? next : ia + Midpoint(fa, null);
    }

    /// <summary><paramref name="count"/> ascending keys strictly between the bounds, spread so none is needlessly long.</summary>
    public static IReadOnlyList<string> KeysBetween(string? before, string? after, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        switch (count)
        {
            case 0:
                return [];
            case 1:
                return [KeyBetween(before, after)];
        }

        if (after is null)
        {
            var keys = new List<string>(count);
            var key = KeyBetween(before, null);
            keys.Add(key);
            for (var i = 1; i < count; i++)
            {
                key = KeyBetween(key, null);
                keys.Add(key);
            }
            return keys;
        }

        if (before is null)
        {
            var keys = new List<string>(count);
            var key = KeyBetween(null, after);
            keys.Add(key);
            for (var i = 1; i < count; i++)
            {
                key = KeyBetween(null, key);
                keys.Add(key);
            }
            keys.Reverse();
            return keys;
        }

        var half = count / 2;
        var middle = KeyBetween(before, after);
        return [.. KeysBetween(before, middle, half), middle, .. KeysBetween(middle, after, count - half - 1)];
    }

    public static bool IsValid(string key)
    {
        try
        {
            Validate(key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Ordinal comparison, the same order as PostgreSQL's "C" collation for these ASCII keys.</summary>
    public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

    private static void Validate(string key)
    {
        if (key.Length == 0 || key.Any(c => Digits.IndexOf(c) < 0))
        {
            throw new ArgumentException($"Invalid order key '{key}': must be non-empty base62.");
        }
        if (key == SmallestInteger)
        {
            throw new ArgumentException($"Invalid order key '{key}'.");
        }

        var integer = IntegerPart(key);
        if (key.Length > integer.Length && key[^1] == Zero)
        {
            throw new ArgumentException($"Invalid order key '{key}': fraction has a trailing zero.");
        }
    }

    private static int IntegerLength(char head) => head switch
    {
        >= 'a' and <= 'z' => head - 'a' + 2,
        >= 'A' and <= 'Z' => 'Z' - head + 2,
        _ => throw new ArgumentException($"Invalid order key head '{head}'."),
    };

    private static string IntegerPart(string key)
    {
        var length = IntegerLength(key[0]);
        if (length > key.Length)
        {
            throw new ArgumentException($"Invalid order key '{key}': integer part is truncated.");
        }
        return key[..length];
    }

    /// <summary>A string strictly between fractions <paramref name="a"/> and <paramref name="b"/> (null = no upper bound).</summary>
    private static string Midpoint(string a, string? b)
    {
        if (b is not null && Compare(a, b) >= 0)
        {
            throw new ArgumentException($"'{a}' must sort before '{b}'.");
        }
        if ((a.Length > 0 && a[^1] == Zero) || (b is { Length: > 0 } && b[^1] == Zero))
        {
            throw new ArgumentException("Fractions must not have trailing zeros.");
        }

        if (b is not null)
        {
            // Strip the longest common prefix, padding a with zeros as we go.
            var n = 0;
            while (n < b.Length && (n < a.Length ? a[n] : Zero) == b[n])
            {
                n++;
            }
            if (n > 0)
            {
                return b[..n] + Midpoint(n < a.Length ? a[n..] : "", b[n..]);
            }
        }

        // The first digits (or the lack of one) differ.
        var digitA = a.Length > 0 ? Digits.IndexOf(a[0]) : 0;
        var digitB = b is not null ? Digits.IndexOf(b[0]) : Digits.Length;
        if (digitB - digitA > 1)
        {
            return Digits[(digitA + digitB + 1) / 2].ToString(); // round half up, as in the reference
        }

        // Consecutive first digits.
        if (b is { Length: > 1 })
        {
            return b[..1];
        }
        return Digits[digitA] + Midpoint(a.Length > 0 ? a[1..] : "", null);
    }

    private static string? Increment(string integer)
    {
        var head = integer[0];
        var digits = integer[1..].ToCharArray().ToList();
        var carry = true;
        for (var i = digits.Count - 1; carry && i >= 0; i--)
        {
            var d = Digits.IndexOf(digits[i]) + 1;
            if (d == Digits.Length)
            {
                digits[i] = Zero;
            }
            else
            {
                digits[i] = Digits[d];
                carry = false;
            }
        }

        if (!carry)
        {
            return head + new string([.. digits]);
        }
        if (head == 'Z')
        {
            return "a" + Zero;
        }
        if (head == 'z')
        {
            return null;
        }

        var nextHead = (char)(head + 1);
        if (nextHead > 'a')
        {
            digits.Add(Zero);
        }
        else
        {
            digits.RemoveAt(digits.Count - 1);
        }
        return nextHead + new string([.. digits]);
    }

    private static string? Decrement(string integer)
    {
        var head = integer[0];
        var digits = integer[1..].ToCharArray().ToList();
        var borrow = true;
        for (var i = digits.Count - 1; borrow && i >= 0; i--)
        {
            var d = Digits.IndexOf(digits[i]) - 1;
            if (d == -1)
            {
                digits[i] = Digits[^1];
            }
            else
            {
                digits[i] = Digits[d];
                borrow = false;
            }
        }

        if (!borrow)
        {
            return head + new string([.. digits]);
        }
        if (head == 'a')
        {
            return "Z" + Digits[^1];
        }
        if (head == 'A')
        {
            return null;
        }

        var previousHead = (char)(head - 1);
        if (previousHead < 'Z')
        {
            digits.Add(Digits[^1]);
        }
        else
        {
            digits.RemoveAt(digits.Count - 1);
        }
        return previousHead + new string([.. digits]);
    }
}
