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
    // Lowercase keys matching Listing.Finishing storage (frontend FILTERS_OPTIONS).
    private static readonly HashSet<string> ValidFinishing = new(StringComparer.OrdinalIgnoreCase)
        { "fully_finished", "semi_finished", "core_shell", "furnished", "unfurnished" };
    private static readonly HashSet<string> ValidPayment = new(StringComparer.OrdinalIgnoreCase)
        { "Cash", "Installment", "Both" };
    private static readonly HashSet<string> ValidCompletion = new(StringComparer.OrdinalIgnoreCase)
        { "Ready", "OffPlan" };
    // Canonical amenity keys — single source of truth for the AI-extracted set.
    // MUST stay in sync with src/data/amenities.ts on the frontend; both sides
    // persist these exact strings (Listing.Amenities is a JSON array of them).
    private static readonly HashSet<string> ValidAmenities = new(StringComparer.Ordinal)
    {
        "elevator", "covered_parking", "natural_gas", "security",
        "backup_generator", "utility_meters", "central_ac",
        "built_in_wardrobes", "maids_room", "balcony", "private_roof",
        "storage_room", "intercom", "internet", "within_compound",
        "shared_pool", "shared_gym", "kids_play_area",
        "landscaped_gardens", "private_garden", "private_pool",
        "private_jacuzzi", "water_view", "landmark_view", "pets_allowed",
    };
    // Cap on AMENITIES_UNKNOWN change-log entries per request — a hostile or
    // confused query could otherwise spam Layer 7 with one row per garbage key.
    private const int MaxAmenityChangesLogged = 5;

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
        if (!string.IsNullOrEmpty(q.CompletionStatus) && !ValidCompletion.Contains(q.CompletionStatus))
        {
            changes.Add(new PlausibilityChange { Field = nameof(q.CompletionStatus),
                ReasonCode = PlausibilityReasons.CompletionUnknown,
                OriginalValue = q.CompletionStatus, NewValue = null });
            q.CompletionStatus = null;
        }

        // Amenities — filter to the canonical-key whitelist. AI is otherwise
        // free to invent keys; if we passed them through, SearchExecutor's
        // JSON-contains query would silently match nothing. Dedupe too, since
        // the model occasionally repeats keys when the user mentions a synonym
        // twice ("بسين ... swimming pool").
        if (q.Amenities is { Count: > 0 } amen)
        {
            var seen   = new HashSet<string>(StringComparer.Ordinal);
            var kept   = new List<string>(amen.Count);
            var logged = 0;
            foreach (var key in amen)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (ValidAmenities.Contains(key))
                {
                    if (seen.Add(key)) kept.Add(key);
                    continue;
                }
                if (logged < MaxAmenityChangesLogged)
                {
                    changes.Add(new PlausibilityChange
                    {
                        Field         = nameof(q.Amenities),
                        ReasonCode    = PlausibilityReasons.AmenitiesUnknown,
                        OriginalValue = key,
                        NewValue      = null,
                    });
                    logged++;
                }
            }
            q.Amenities = kept.Count == 0 ? null : kept;
        }
        else if (q.Amenities is { Count: 0 })
        {
            // Schema requires the field; null is the canonical "user didn't say".
            q.Amenities = null;
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
        CompletionStatus  = src.CompletionStatus,
        Amenities         = src.Amenities is null ? null : new List<string>(src.Amenities),
    };
}
