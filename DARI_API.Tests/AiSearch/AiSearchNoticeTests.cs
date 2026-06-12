using System.Collections.Generic;
using DARI_API.AiSearch;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Guards the "never silently drop the user's location" behaviour. The bug this
// locks in: an unknown place the model puts in the free-text field (location =
// null) used to return unrelated listings with no notice, because the only
// check was for a dropped *city* (CITY_UNKNOWN).
public class AiSearchNoticeTests
{
    private static List<PlausibilityChange> NoChanges() => new();

    private static List<PlausibilityChange> CityDropped(string original) => new()
    {
        new PlausibilityChange
        {
            Field = "Location",
            ReasonCode = PlausibilityReasons.CityUnknown,
            OriginalValue = original,
            NewValue = null,
        },
    };

    // ── ResolveUnresolvedArea ───────────────────────────────────────────────

    [Fact]
    public void Unresolved_when_freetext_only_and_nothing_matched()
    {
        // "apartment in Tokyo" — model puts the place in LocationText, no city.
        var q = new ParsedQuery { Location = null, LocationText = "طوكيو اليابان" };
        var area = AiSearchPipeline.ResolveUnresolvedArea(NoChanges(), q, MatchQuality.None);
        Assert.Equal("طوكيو اليابان", area);
    }

    [Fact]
    public void Unresolved_uses_dropped_city_from_plausibility()
    {
        var q = new ParsedQuery { Location = null, LocationText = null };
        var area = AiSearchPipeline.ResolveUnresolvedArea(CityDropped("Atlantis"), q, MatchQuality.None);
        Assert.Equal("Atlantis", area);
    }

    [Fact]
    public void NotUnresolved_when_city_was_resolved()
    {
        // A known city is set — the street-fallback notice covers any miss, so
        // this is not an "unresolved area".
        var q = new ParsedQuery { Location = "Nasr City", LocationText = "شارع مجهول" };
        var area = AiSearchPipeline.ResolveUnresolvedArea(NoChanges(), q, MatchQuality.None);
        Assert.Null(area);
    }

    [Fact]
    public void NotUnresolved_when_freetext_matched_something()
    {
        // LocationText matched a region/street (bestMatch better than None).
        var q = new ParsedQuery { Location = null, LocationText = "Zamalek" };
        var area = AiSearchPipeline.ResolveUnresolvedArea(NoChanges(), q, MatchQuality.Exact);
        Assert.Null(area);
    }

    [Fact]
    public void NotUnresolved_when_no_location_given()
    {
        var q = new ParsedQuery { Location = null, LocationText = null };
        var area = AiSearchPipeline.ResolveUnresolvedArea(NoChanges(), q, MatchQuality.None);
        Assert.Null(area);
    }

    // ── BuildNotice (unknown-area case) ─────────────────────────────────────

    [Fact]
    public void Notice_unknown_area_with_results_is_localized()
    {
        var q = new ParsedQuery();
        var ar = AiSearchPipeline.BuildNotice("none", false, 8, q, "طوكيو اليابان", "ar");
        var en = AiSearchPipeline.BuildNotice("none", false, 8, q, "Tokyo", "en");

        Assert.Contains("طوكيو اليابان", ar);
        Assert.Contains("كل المناطق", ar);          // "...showing results from all areas"
        Assert.Contains("Tokyo", en);
        Assert.Contains("all areas", en);
    }

    [Fact]
    public void Notice_unknown_area_with_zero_results_suggests_a_known_area()
    {
        var q = new ParsedQuery();
        var ar = AiSearchPipeline.BuildNotice("none", false, 0, q, "طوكيو", "ar");
        var en = AiSearchPipeline.BuildNotice("none", false, 0, q, "Tokyo", "en");

        Assert.Contains("طوكيو", ar);
        Assert.Contains("منطقة معروفة", ar);         // "...try a known area name"
        Assert.Contains("known area", en);
    }

    [Fact]
    public void Notice_null_when_everything_matches_cleanly()
    {
        var q = new ParsedQuery { Location = "Nasr City" };
        var notice = AiSearchPipeline.BuildNotice("exact", false, 5, q, null, "ar");
        Assert.Null(notice);
    }
}
