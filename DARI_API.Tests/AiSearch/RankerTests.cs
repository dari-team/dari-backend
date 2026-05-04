using DARI_API.AiSearch;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Tier-by-tier verification of Problem 5's invariants.
//
//   T1: match_quality dominates everything else.
//   T2: bedroom proximity to suggested_bedrooms — only when the user supplied one.
//   T3: price proximity — only when at least one price bound is set.
//
// Each test isolates ONE invariant by holding the others equal. The
// non-blend tests (T1 outranks perfect-fit T3) are the load-bearing ones.
public class RankerTests
{
    private static readonly Dictionary<string, Guid> IdCache = new();
    private static SearchCandidate Make(
        string id, MatchQuality match, int beds = 3, decimal price = 1_000_000m, int views = 0)
    {
        if (!IdCache.TryGetValue(id, out var guid))
        {
            guid = Guid.NewGuid();
            IdCache[id] = guid;
        }
        return new SearchCandidate
        {
            ListingId = guid,
            Match     = match,
            FtsRank   = match == MatchQuality.Exact ? 100 : (match == MatchQuality.Partial ? 10 : 0),
            Bedrooms  = beds,
            Price     = price,
            ViewCount = views,
        };
    }

    // ── T1 dominance: the load-bearing invariant ──────────────────────────

    [Fact]
    public void T1_exact_outranks_T1_partial_with_perfect_other_tiers()
    {
        // Partial-match listing has a perfect price and perfect bedroom fit.
        // Exact-match listing has terrible price and terrible bedroom fit.
        // T1 must still win.
        var partial = Make("partial", MatchQuality.Partial, beds: 3, price: 1_000_000m);
        var exact   = Make("exact",   MatchQuality.Exact,   beds: 9, price: 9_999_999m);

        var query = new ParsedQuery
        {
            SuggestedBedrooms = 3,
            PriceMin = 950_000m, PriceMax = 1_050_000m,
        };

        var ranked = Ranker.Rank(new[] { partial, exact }, query);
        Assert.Equal(exact.ListingId, ranked[0].ListingId);
    }

    [Fact]
    public void T1_partial_outranks_T1_none_with_perfect_other_tiers()
    {
        var none    = Make("none",    MatchQuality.None,    beds: 3, price: 1_000_000m);
        var partial = Make("partial", MatchQuality.Partial, beds: 9, price: 9_999_999m);

        var query = new ParsedQuery
        {
            SuggestedBedrooms = 3,
            PriceMin = 950_000m, PriceMax = 1_050_000m,
        };

        var ranked = Ranker.Rank(new[] { none, partial }, query);
        Assert.Equal(partial.ListingId, ranked[0].ListingId);
    }

    // ── T2: bedroom proximity ──────────────────────────────────────────────

    [Fact]
    public void T2_bedroom_proximity_orders_within_same_T1()
    {
        // Both Exact-match. User wants suggested 3. The 3-bedroom should win.
        var b3 = Make("b3", MatchQuality.Exact, beds: 3, price: 1_000_000m);
        var b5 = Make("b5", MatchQuality.Exact, beds: 5, price: 1_000_000m);

        var query = new ParsedQuery { SuggestedBedrooms = 3 };

        var ranked = Ranker.Rank(new[] { b5, b3 }, query);
        Assert.Equal(b3.ListingId, ranked[0].ListingId);
    }

    [Fact]
    public void T2_inactive_when_suggested_bedrooms_is_null()
    {
        // No family-size signal → T2 must not reorder.
        // We rely on stable sorting: tiebreaker is view_count desc.
        var b3 = Make("b3", MatchQuality.Exact, beds: 3, price: 1_000_000m, views: 5);
        var b5 = Make("b5", MatchQuality.Exact, beds: 5, price: 1_000_000m, views: 99);

        var query = new ParsedQuery();   // no suggested_bedrooms

        var ranked = Ranker.Rank(new[] { b3, b5 }, query);
        // view_count desc means b5 (99) ranks first.
        Assert.Equal(b5.ListingId, ranked[0].ListingId);
    }

    // ── T3: price proximity ────────────────────────────────────────────────

    [Fact]
    public void T3_price_in_range_outranks_out_of_range_within_same_T1_T2()
    {
        var inRange  = Make("in",  MatchQuality.Exact, beds: 3, price: 1_000_000m);
        var outOver  = Make("out", MatchQuality.Exact, beds: 3, price: 5_000_000m);

        var query = new ParsedQuery { PriceMax = 1_500_000m };

        var ranked = Ranker.Rank(new[] { outOver, inRange }, query);
        Assert.Equal(inRange.ListingId, ranked[0].ListingId);
    }

    [Fact]
    public void T3_inactive_when_no_price_bounds()
    {
        var cheap   = Make("cheap",   MatchQuality.Exact, beds: 3, price:    100_000m, views: 1);
        var expensive = Make("exp",  MatchQuality.Exact, beds: 3, price: 50_000_000m, views: 50);

        var query = new ParsedQuery();   // no price bounds

        var ranked = Ranker.Rank(new[] { cheap, expensive }, query);
        // Neither price gets a penalty, so view_count breaks the tie.
        Assert.Equal(expensive.ListingId, ranked[0].ListingId);
    }

    // ── Tiebreaker: view_count desc ───────────────────────────────────────

    [Fact]
    public void Tiebreaker_view_count_desc_when_all_tiers_tie()
    {
        var a = Make("a", MatchQuality.Exact, beds: 3, price: 1_000_000m, views: 10);
        var b = Make("b", MatchQuality.Exact, beds: 3, price: 1_000_000m, views: 100);

        var query = new ParsedQuery { SuggestedBedrooms = 3, PriceMax = 1_500_000m };

        var ranked = Ranker.Rank(new[] { a, b }, query);
        Assert.Equal(b.ListingId, ranked[0].ListingId);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void Empty_input_returns_empty_output()
    {
        var ranked = Ranker.Rank(Array.Empty<SearchCandidate>(), new ParsedQuery());
        Assert.Empty(ranked);
    }

    [Fact]
    public void Single_candidate_passes_through()
    {
        var c = Make("only", MatchQuality.Partial);
        var ranked = Ranker.Rank(new[] { c }, new ParsedQuery());
        Assert.Single(ranked);
        Assert.Equal(c.ListingId, ranked[0].ListingId);
    }

    // ── The non-blend invariant — phrased the way the design log states it.

    [Fact]
    public void NonBlend_invariant_T1_always_dominates_regardless_of_T2_T3_perfection()
    {
        // The design log: "A listing on the correct street with a bad price
        // must outrank a listing on the wrong street with a perfect price,
        // ALWAYS. This is enforced by the design — not a weight."
        //
        // We try the full grid: every combination of T2/T3 perfection on the
        // wrong-street row vs. T2/T3 disaster on the right-street row.
        var query = new ParsedQuery
        {
            SuggestedBedrooms = 3,
            PriceMin = 950_000m, PriceMax = 1_050_000m,
        };

        // Right street, every T2/T3 violation imaginable.
        var rightStreet = Make("right", MatchQuality.Exact, beds: 9, price: 50_000_000m, views: 0);

        // Wrong street, perfect T2/T3 plus huge view_count.
        var wrongStreet = Make("wrong", MatchQuality.None,  beds: 3, price: 1_000_000m,   views: 9999);

        // Even partial match wins over none.
        var partialStreet = Make("partial", MatchQuality.Partial, beds: 9, price: 50_000_000m);

        var ranked = Ranker.Rank(new[] { wrongStreet, rightStreet, partialStreet }, query);

        Assert.Equal(rightStreet.ListingId,   ranked[0].ListingId);
        Assert.Equal(partialStreet.ListingId, ranked[1].ListingId);
        Assert.Equal(wrongStreet.ListingId,   ranked[2].ListingId);
    }
}
