namespace DARI_API.AiSearch;

// Layer 3 — Deterministic post-processor for AI output.
// Purpose: catch semantic errors (valid JSON, wrong meaning) the AI can produce
// even when it follows the schema. NO AI calls here — pure rules, fully
// testable, fully predictable.
//
// Rules apply in this order; each rule may emit a PlausibilityChange so
// Layer 7 can log what we corrected and why.
public static class PlausibilityChecker
{
    // Hard caps — anything past these is almost certainly an AI hallucination
    // or a unit confusion (e.g. "1000 sqm villa" → 1000 bedrooms).
    private const int MaxBedrooms     = 10;
    private const int MaxBathrooms    = 10;
    private static readonly HashSet<string> ValidProperty = new(StringComparer.OrdinalIgnoreCase)
        { "apartment", "villa", "studio", "duplex", "penthouse" };
    private static readonly HashSet<string> ValidFinishing = new(StringComparer.OrdinalIgnoreCase)
        { "CoreAndShell", "SemiFinished", "FullyFinished", "Unfurnished", "Furnished" };
    private static readonly HashSet<string> ValidPayment = new(StringComparer.OrdinalIgnoreCase)
        { "Cash", "Installment", "Both" };

    public static PlausibilityResult Check(ParsedQuery input)
    {
        var changes = new List<PlausibilityChange>();
        var q = Clone(input);

        // Rule 1 — City validation. Unknown city is silently dropped so the
        // search falls back to text-only (Layer 4 still has location_text).
        if (!string.IsNullOrWhiteSpace(q.Location) && !KnownCities.IsKnown(q.Location))
        {
            changes.Add(new PlausibilityChange
            {
                Field         = nameof(q.Location),
                ReasonCode    = PlausibilityReasons.CityUnknown,
                OriginalValue = q.Location,
                NewValue      = null,
            });
            q.Location = null;
        }
        else if (!string.IsNullOrWhiteSpace(q.Location))
        {
            // Canonicalize "nasr city" → "Nasr City" for stable downstream matching.
            q.Location = KnownCities.Canonicalize(q.Location);
        }

        // Rule 2 — Bedroom ceiling.
        if (q.Bedrooms is int b && b > MaxBedrooms)
        {
            changes.Add(new PlausibilityChange
            {
                Field         = nameof(q.Bedrooms),
                ReasonCode    = PlausibilityReasons.BedroomsAbsurd,
                OriginalValue = b.ToString(),
                NewValue      = null,
            });
            q.Bedrooms = null;
        }
        if (q.Bedrooms is int bn && bn < 0)
        {
            q.Bedrooms = null;
            changes.Add(new PlausibilityChange
            {
                Field = nameof(q.Bedrooms), ReasonCode = PlausibilityReasons.BedroomsAbsurd,
                OriginalValue = bn.ToString(), NewValue = null,
            });
        }

        if (q.Bathrooms is int ba && ba > MaxBathrooms)
        {
            changes.Add(new PlausibilityChange
            {
                Field = nameof(q.Bathrooms), ReasonCode = PlausibilityReasons.BathroomsAbsurd,
                OriginalValue = ba.ToString(), NewValue = null,
            });
            q.Bathrooms = null;
        }

        // Rule 3 — Bedroom conflict: explicit count wins, drop family-size signal.
        if (q.Bedrooms is not null && q.SuggestedBedrooms is not null)
        {
            changes.Add(new PlausibilityChange
            {
                Field         = nameof(q.SuggestedBedrooms),
                ReasonCode    = PlausibilityReasons.BedroomsConflict,
                OriginalValue = q.SuggestedBedrooms.ToString(),
                NewValue      = null,
            });
            q.SuggestedBedrooms = null;
        }

        // Rule 4 — Price inversion: swap, don't drop.
        if (q.PriceMin is decimal min && q.PriceMax is decimal max && min > max)
        {
            changes.Add(new PlausibilityChange
            {
                Field         = "PriceRange",
                ReasonCode    = PlausibilityReasons.PriceInverted,
                OriginalValue = $"min={min}, max={max}",
                NewValue      = $"min={max}, max={min}",
            });
            (q.PriceMin, q.PriceMax) = (max, min);
        }

        // Negative prices — drop (rare, but AI sometimes produces them).
        if (q.PriceMin < 0)
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.PriceMin),
                ReasonCode = PlausibilityReasons.PriceNegative,
                OriginalValue = q.PriceMin.ToString(), NewValue = null });
            q.PriceMin = null;
        }
        if (q.PriceMax < 0)
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.PriceMax),
                ReasonCode = PlausibilityReasons.PriceNegative,
                OriginalValue = q.PriceMax.ToString(), NewValue = null });
            q.PriceMax = null;
        }

        // Rule 5 — Empty street text.
        if (!string.IsNullOrWhiteSpace(q.LocationText) && q.LocationText.Trim().Length == 0)
        {
            changes.Add(new PlausibilityChange
            {
                Field         = nameof(q.LocationText),
                ReasonCode    = PlausibilityReasons.StreetEmpty,
                OriginalValue = q.LocationText,
                NewValue      = null,
            });
            q.LocationText = null;
        }
        // Also collapse the case where the AI returned only whitespace inside
        // a string (we got a non-null but useless value).
        if (q.LocationText is { } lt && string.IsNullOrWhiteSpace(lt))
        {
            q.LocationText = null;
        }

        // Defensive — enums returned by AI: drop if not in the allowed set.
        if (!string.IsNullOrEmpty(q.PropertyType) && !ValidProperty.Contains(q.PropertyType))
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.PropertyType),
                ReasonCode = PlausibilityReasons.PropertyUnknown,
                OriginalValue = q.PropertyType, NewValue = null });
            q.PropertyType = null;
        }
        if (!string.IsNullOrEmpty(q.FinishingLevel) && !ValidFinishing.Contains(q.FinishingLevel))
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.FinishingLevel),
                ReasonCode = PlausibilityReasons.FinishingUnknown,
                OriginalValue = q.FinishingLevel, NewValue = null });
            q.FinishingLevel = null;
        }
        if (!string.IsNullOrEmpty(q.PaymentMethod) && !ValidPayment.Contains(q.PaymentMethod))
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.PaymentMethod),
                ReasonCode = PlausibilityReasons.PaymentUnknown,
                OriginalValue = q.PaymentMethod, NewValue = null });
            q.PaymentMethod = null;
        }

        return new PlausibilityResult { Cleaned = q, Changes = changes };
    }

    private static ParsedQuery Clone(ParsedQuery src) => new()
    {
        PropertyType      = src.PropertyType,
        ListingType       = src.ListingType,
        Location          = src.Location,
        LocationText      = src.LocationText,
        NearMetro         = src.NearMetro,
        PriceMin          = src.PriceMin,
        PriceMax          = src.PriceMax,
        Bedrooms          = src.Bedrooms,
        Bathrooms         = src.Bathrooms,
        SuggestedBedrooms = src.SuggestedBedrooms,
        AreaMin           = src.AreaMin,
        FinishingLevel    = src.FinishingLevel,
        PaymentMethod     = src.PaymentMethod,
        MaxDownPayment    = src.MaxDownPayment,
    };
}
