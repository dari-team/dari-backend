using DARI_API.AiSearch;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Tests Layer 3's rule-by-rule contract. Each rule has a "fires" case and
// a "doesn't fire" case so we know both the trigger and the no-op work.
public class PlausibilityCheckerTests
{
    private static ParsedQuery Empty() => new();

    // ── CITY_UNKNOWN ───────────────────────────────────────────────────────

    [Fact]
    public void CityUnknown_drops_unknown_city_and_logs_change()
    {
        var q = Empty();
        q.Location = "Atlantis";
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.Location);
        Assert.Single(r.Changes);
        Assert.Equal(PlausibilityReasons.CityUnknown, r.Changes[0].ReasonCode);
        Assert.Equal("Atlantis", r.Changes[0].OriginalValue);
    }

    [Fact]
    public void CityUnknown_does_not_fire_for_known_city()
    {
        var q = Empty();
        q.Location = "Maadi";
        var r = PlausibilityChecker.Check(q);

        Assert.Equal("Maadi", r.Cleaned.Location);
        Assert.DoesNotContain(r.Changes, c => c.ReasonCode == PlausibilityReasons.CityUnknown);
    }

    [Fact]
    public void Known_city_is_canonicalized_to_proper_casing()
    {
        var q = Empty();
        q.Location = "nasr-city";
        var r = PlausibilityChecker.Check(q);

        Assert.Equal("Nasr City", r.Cleaned.Location);
        Assert.DoesNotContain(r.Changes, c => c.ReasonCode == PlausibilityReasons.CityUnknown);
    }

    // ── BEDROOMS_ABSURD ────────────────────────────────────────────────────

    [Fact]
    public void BedroomsAbsurd_drops_absurd_count()
    {
        var q = Empty();
        q.Bedrooms = 99;
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.Bedrooms);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.BedroomsAbsurd);
    }

    [Fact]
    public void BedroomsAbsurd_does_not_fire_for_reasonable_count()
    {
        var q = Empty();
        q.Bedrooms = 4;
        var r = PlausibilityChecker.Check(q);

        Assert.Equal(4, r.Cleaned.Bedrooms);
        Assert.DoesNotContain(r.Changes, c => c.ReasonCode == PlausibilityReasons.BedroomsAbsurd);
    }

    // ── BEDROOMS_CONFLICT ──────────────────────────────────────────────────

    [Fact]
    public void BedroomsConflict_keeps_explicit_drops_suggested()
    {
        var q = Empty();
        q.Bedrooms          = 3;
        q.SuggestedBedrooms = 2;
        var r = PlausibilityChecker.Check(q);

        Assert.Equal(3, r.Cleaned.Bedrooms);
        Assert.Null(r.Cleaned.SuggestedBedrooms);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.BedroomsConflict);
    }

    [Fact]
    public void BedroomsConflict_does_not_fire_when_only_suggested_is_set()
    {
        var q = Empty();
        q.SuggestedBedrooms = 2;
        var r = PlausibilityChecker.Check(q);

        Assert.Equal(2, r.Cleaned.SuggestedBedrooms);
        Assert.DoesNotContain(r.Changes, c => c.ReasonCode == PlausibilityReasons.BedroomsConflict);
    }

    // ── PRICE_INVERTED ─────────────────────────────────────────────────────

    [Fact]
    public void PriceInverted_swaps_min_and_max()
    {
        var q = Empty();
        q.PriceMin = 5_000_000m;
        q.PriceMax = 1_000_000m;
        var r = PlausibilityChecker.Check(q);

        Assert.Equal(1_000_000m, r.Cleaned.PriceMin);
        Assert.Equal(5_000_000m, r.Cleaned.PriceMax);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.PriceInverted);
    }

    [Fact]
    public void PriceInverted_does_not_fire_for_correct_order()
    {
        var q = Empty();
        q.PriceMin = 1_000_000m;
        q.PriceMax = 5_000_000m;
        var r = PlausibilityChecker.Check(q);

        Assert.Equal(1_000_000m, r.Cleaned.PriceMin);
        Assert.Equal(5_000_000m, r.Cleaned.PriceMax);
        Assert.DoesNotContain(r.Changes, c => c.ReasonCode == PlausibilityReasons.PriceInverted);
    }

    // ── STREET_EMPTY ───────────────────────────────────────────────────────

    [Fact]
    public void StreetEmpty_drops_whitespace_only_text()
    {
        var q = Empty();
        q.LocationText = "    ";
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.LocationText);
    }

    [Fact]
    public void StreetEmpty_keeps_real_text()
    {
        var q = Empty();
        q.LocationText = "عباس العقاد";
        var r = PlausibilityChecker.Check(q);

        Assert.Equal("عباس العقاد", r.Cleaned.LocationText);
    }

    // ── ENUM VALIDATION ────────────────────────────────────────────────────

    [Fact]
    public void Unknown_property_type_is_dropped()
    {
        var q = Empty();
        q.PropertyType = "spaceship";
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.PropertyType);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.PropertyUnknown);
    }

    [Fact]
    public void Unknown_finishing_level_is_dropped()
    {
        var q = Empty();
        q.FinishingLevel = "GoldPlated";
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.FinishingLevel);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.FinishingUnknown);
    }

    [Fact]
    public void Valid_finishing_level_passes_through()
    {
        var q = Empty();
        q.FinishingLevel = "FullyFinished";
        var r = PlausibilityChecker.Check(q);

        Assert.Equal("FullyFinished", r.Cleaned.FinishingLevel);
    }

    [Fact]
    public void Negative_price_is_dropped()
    {
        var q = Empty();
        q.PriceMin = -100m;
        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.PriceMin);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.PriceNegative);
    }

    // ── INTEGRATION: multiple rules at once ───────────────────────────────

    [Fact]
    public void Multiple_violations_each_log_one_change()
    {
        var q = Empty();
        q.Location          = "Atlantis";        // CITY_UNKNOWN
        q.Bedrooms          = 99;                // BEDROOMS_ABSURD
        q.SuggestedBedrooms = 3;                 // dropped after Bedrooms is dropped — Conflict won't fire
        q.PriceMin          = 5_000_000m;
        q.PriceMax          = 1_000_000m;        // PRICE_INVERTED
        q.LocationText      = "  ";
        q.PropertyType      = "spaceship";       // PROPERTY_UNKNOWN

        var r = PlausibilityChecker.Check(q);

        Assert.Null(r.Cleaned.Location);
        Assert.Null(r.Cleaned.Bedrooms);
        Assert.Equal(3, r.Cleaned.SuggestedBedrooms);  // suggested kept since Bedrooms got dropped
        Assert.Equal(1_000_000m, r.Cleaned.PriceMin);  // swapped
        Assert.Equal(5_000_000m, r.Cleaned.PriceMax);
        Assert.Null(r.Cleaned.LocationText);
        Assert.Null(r.Cleaned.PropertyType);

        // Change log mentions each violation by reason code.
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.CityUnknown);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.BedroomsAbsurd);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.PriceInverted);
        Assert.Contains(r.Changes, c => c.ReasonCode == PlausibilityReasons.PropertyUnknown);
    }

    [Fact]
    public void Empty_input_produces_empty_output_with_no_changes()
    {
        var r = PlausibilityChecker.Check(Empty());
        Assert.Empty(r.Changes);
        Assert.Null(r.Cleaned.Location);
        Assert.Null(r.Cleaned.Bedrooms);
    }
}
