namespace Cv.Core.Ordering;

/// <summary>
/// Assigns sort keys to a list of siblings in their new order while keeping as many existing
/// keys as possible. Only the siblings that actually moved, or are new, get new keys, so a
/// version diff reports one change for one move instead of renumbering every sibling.
/// </summary>
public static class SiblingOrder
{
    /// <param name="existingKeys">
    /// For each sibling, in the desired order: its key in the base version (if it had the same
    /// parent there), or null for a new or re-parented node.
    /// </param>
    /// <returns>Keys in the same order, strictly ascending.</returns>
    /// <remarks>
    /// The kept keys are a longest strictly increasing subsequence of the existing keys: the
    /// largest set of siblings whose relative order did not change. Every other sibling gets a
    /// fresh key between its kept neighbours.
    /// </remarks>
    public static IReadOnlyList<string> Assign(IReadOnlyList<string?> existingKeys)
    {
        var keep = LongestIncreasingRun(existingKeys);
        var result = new string[existingKeys.Count];

        var i = 0;
        string? lowerBound = null;
        while (i < existingKeys.Count)
        {
            if (keep[i])
            {
                lowerBound = result[i] = existingKeys[i]!;
                i++;
                continue;
            }

            var runStart = i;
            while (i < existingKeys.Count && !keep[i])
            {
                i++;
            }
            var upperBound = i < existingKeys.Count ? existingKeys[i] : null;

            var fresh = FractionalIndex.KeysBetween(lowerBound, upperBound, i - runStart);
            for (var j = 0; j < fresh.Count; j++)
            {
                result[runStart + j] = fresh[j];
            }
            lowerBound = fresh[^1];
        }

        return result;
    }

    /// <summary>Marks a longest strictly increasing subsequence of the valid, non-null keys (patience sorting, O(n log n)).</summary>
    private static bool[] LongestIncreasingRun(IReadOnlyList<string?> keys)
    {
        var keep = new bool[keys.Count];
        var tails = new List<int>();          // tails[k] = index ending the best run of length k + 1
        var previous = new int[keys.Count];   // predecessor of each index in its run

        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] is not { } key || !FractionalIndex.IsValid(key))
            {
                continue;
            }

            // First tail whose key is >= this key (strictly increasing, so equal keys don't extend).
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (FractionalIndex.Compare(keys[tails[mid]]!, key) < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[lo] = i;
            }
        }

        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
        {
            keep[i] = true;
        }
        return keep;
    }
}
