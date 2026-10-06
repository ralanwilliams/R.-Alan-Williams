using Cv.Core.Ordering;

namespace Cv.Core.Tests.Ordering;

public class SiblingOrderTests
{
    private static void AssertAscending(IReadOnlyList<string> keys)
    {
        for (var i = 1; i < keys.Count; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1], keys[i]) < 0, $"{keys[i - 1]} !< {keys[i]}");
        }
    }

    [Fact]
    public void Unchanged_order_keeps_every_key()
    {
        string[] existing = ["a0", "a1", "a2", "a3"];
        Assert.Equal(existing, SiblingOrder.Assign(existing));
    }

    [Fact]
    public void Inserting_at_the_top_changes_only_the_new_row()
    {
        // The point of fractional indexing (ADR 0001 §8): a diff shows one change, not a renumbering.
        var keys = SiblingOrder.Assign([null, "a0", "a1", "a2"]);

        Assert.Equal(["a0", "a1", "a2"], keys.Skip(1));
        AssertAscending(keys);
    }

    [Fact]
    public void Moving_one_row_changes_only_that_row()
    {
        // a3 moved from last to second.
        var keys = SiblingOrder.Assign(["a0", "a3", "a1", "a2"]);

        Assert.Equal("a0", keys[0]);
        Assert.NotEqual("a3", keys[1]);
        Assert.Equal(["a1", "a2"], keys.Skip(2));
        AssertAscending(keys);
    }

    [Fact]
    public void Reversing_keeps_one_key_and_still_orders_correctly()
    {
        string[] existing = ["a3", "a2", "a1", "a0"];
        var keys = SiblingOrder.Assign(existing);

        Assert.Single(keys.Where((key, i) => key == existing[i]));
        AssertAscending(keys);
    }

    [Fact]
    public void New_siblings_get_fresh_ascending_keys()
    {
        var keys = SiblingOrder.Assign([null, null, null]);

        Assert.Equal(3, keys.Count);
        AssertAscending(keys);
    }

    [Fact]
    public void Duplicate_or_invalid_existing_keys_are_replaced()
    {
        var keys = SiblingOrder.Assign(["a1", "a1", "not base62!", "a2"]);

        AssertAscending(keys);
        Assert.All(keys, k => Assert.True(FractionalIndex.IsValid(k)));
    }

    [Fact]
    public void Random_reorderings_always_produce_ascending_valid_keys()
    {
        var random = new Random(99);
        for (var round = 0; round < 200; round++)
        {
            var original = FractionalIndex.KeysBetween(null, null, random.Next(1, 12));
            var shuffled = original.OrderBy(_ => random.Next())
                .Select(k => random.Next(5) == 0 ? null : k) // some rows are new
                .ToList();

            var keys = SiblingOrder.Assign(shuffled);

            AssertAscending(keys);
            Assert.All(keys, k => Assert.True(FractionalIndex.IsValid(k)));
        }
    }
}
