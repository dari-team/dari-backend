namespace DARI_API.AiSearch;

// Layer 5 — three-tier ranking. Tiers are NEVER blended into a single score.
// Comparison cascades: Tier 1 first; on tie, Tier 2; on tie, Tier 3; on tie,
// view_count.
//
// This is THE design invariant from Problem 5: a row that hit the user's
// street (T1.Exact) always outranks a row on a different street with a
// perfect price match. Encoded as a tuple comparator, not a weighted score
// — there's no weight that could make T1 lose to T3.
public static class Ranker
{
    public static List<SearchCandidate> Rank(
        IEnumerable<SearchCandidate> candidates,
        ParsedQuery query)
    {
        return candidates
            .OrderBy(c => (int)c.Match)                                    // T1 — exact (0) < partial (1) < none (2)
            .ThenBy(c => BedroomDistance(c, query))                        // T2
            .ThenBy(c => PriceDistance(c, query))                          // T3
            .ThenByDescending(c => c.ViewCount)                            // tiebreaker
            .ToList();
    }

    // T2: distance to suggested_bedrooms. Inactive (returns 0 for everyone)
    // when the user didn't supply a family-size signal — so it doesn't
    // accidentally re-order results.
    private static int BedroomDistance(SearchCandidate c, ParsedQuery q)
    {
        if (q.SuggestedBedrooms is not int target) return 0;
        return Math.Abs(c.Bedrooms - target);
    }

    // T3: how far the listing's price falls outside [PriceMin, PriceMax].
    // 0 = in range. Positive = distance from the violated bound. Inactive
    // (returns 0) when neither bound was supplied.
    private static decimal PriceDistance(SearchCandidate c, ParsedQuery q)
    {
        if (q.PriceMin is null && q.PriceMax is null) return 0;
        if (q.PriceMin is decimal min && c.Price < min) return min - c.Price;
        if (q.PriceMax is decimal max && c.Price > max) return c.Price - max;
        return 0;
    }
}
