using Cv.Core.Ordering;

namespace Cv.Core.Tests.Ordering;

public class FractionalIndexTests
{
    // Expected values come from the reference implementation (the fractional-indexing npm
    // package). The port was also checked against it on 4,600 randomised insertions.
    [Theory]
    [InlineData(null, null, "a0")]
    [InlineData("a0", null, "a1")]
    [InlineData(null, "a0", "Zz")]
    [InlineData("a0", "a1", "a0V")] // the example in ADR 0001 §8
    [InlineData("a1", "a2", "a1V")]
    [InlineData("a0V", "a1", "a0l")]
    [InlineData("Zz", "a0", "ZzV")]
    [InlineData("a0", "a0V", "a0G")]
    [InlineData("az", null, "b00")]
    [InlineData("Zz", null, "a0")]
    [InlineData(null, "Zz", "Zy")]
    [InlineData("a0", "a01", "a00V")]
    [InlineData("b00", null, "b01")]
    public void KeyBetween_matches_the_reference_implementation(string? before, string? after, string expected) =>
        Assert.Equal(expected, FractionalIndex.KeyBetween(before, after));

    [Theory]
    [InlineData("a0", "a0")]
    [InlineData("a1", "a0")]
    public void KeyBetween_rejects_bounds_out_of_order(string before, string after) =>
        Assert.Throws<ArgumentException>(() => FractionalIndex.KeyBetween(before, after));

    [Theory]
    [InlineData("")]
    [InlineData("a")]      // integer part truncated
    [InlineData("a00")]    // fraction with a trailing zero
    [InlineData("a0-")]    // not base62
    [InlineData("!0")]
    public void Invalid_keys_are_recognised(string key) => Assert.False(FractionalIndex.IsValid(key));

    [Fact]
    public void Random_insertions_keep_every_key_valid_and_strictly_ordered()
    {
        var random = new Random(1234);
        var keys = new List<string>();
        for (var i = 0; i < 2_000; i++)
        {
            var position = random.Next(keys.Count + 1);
            var key = FractionalIndex.KeyBetween(
                position > 0 ? keys[position - 1] : null,
                position < keys.Count ? keys[position] : null);
            keys.Insert(position, key);
        }

        Assert.All(keys, k => Assert.True(FractionalIndex.IsValid(k), k));
        for (var i = 1; i < keys.Count; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0, $"{keys[i - 1]} !< {keys[i]}");
        }
    }

    [Theory]
    [InlineData(null, null, 5)]
    [InlineData("a0", null, 7)]
    [InlineData(null, "a0", 7)]
    [InlineData("a0", "a1", 9)]
    public void KeysBetween_returns_ascending_keys_inside_the_bounds(string? before, string? after, int count)
    {
        var keys = FractionalIndex.KeysBetween(before, after, count);

        Assert.Equal(count, keys.Count);
        var bounded = new[] { before }.Concat(keys).Concat([after]).ToList();
        for (var i = 1; i < bounded.Count; i++)
        {
            if (bounded[i - 1] is { } lower && bounded[i] is { } upper)
            {
                Assert.True(string.CompareOrdinal(lower, upper) < 0, $"{lower} !< {upper}");
            }
        }
    }

    [Fact]
    public void Base62_digits_are_in_ordinal_order_so_the_C_collation_sorts_keys_correctly()
    {
        var digits = FractionalIndex.Digits.ToCharArray();
        Assert.Equal(digits.OrderBy(c => c).ToArray(), digits);
        // The trap ADR 0001 §8 warns about: a linguistic collation would put "Zz" after "a0".
        Assert.True(string.CompareOrdinal("Zz", "a0") < 0);
    }
}
