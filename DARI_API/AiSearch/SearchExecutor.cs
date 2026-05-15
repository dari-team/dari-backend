using DARI_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DARI_API.AiSearch;

public enum MatchQuality
{
    Exact   = 0,    // strong FTS rank on the user's street keywords
    Partial = 1,    // weak FTS rank
    None    = 2,    // city/region match only — fallback path
}

// One row of search output. Carries enough metadata for Layer 5 (ranking)
// without re-querying the DB. The full Listing is loaded later, after ranking
// trims the candidate set.
public class SearchCandidate
{
    public required Guid ListingId       { get; set; }
    public required MatchQuality Match   { get; set; }
    public double FtsRank                { get; set; }   // 0 when MatchQuality = None
    public int Bedrooms                  { get; set; }
    public decimal Price                 { get; set; }
    public int ViewCount                 { get; set; }
    public string? City                  { get; set; }
    public string? Region                { get; set; }
}

// Layer 4 — translates a cleaned ParsedQuery into a candidate list.
//
// Mandatory filter: city (else fallback to text-only).
// Optional hard filter: bedrooms (explicit count only — suggested_bedrooms
// is NEVER promoted to a hard filter; that's a Layer 5 ranking signal).
// Optional FTS: when location_text is set, we search StreetSearchKey via
// CONTAINSTABLE on the Buckwalter-normalized form. Hits get MatchQuality
// from a rank threshold; rows that match the city but not the FTS query
// fall through as MatchQuality = None.
public class SearchExecutor
{
    private readonly Models.ApplicationDbContext _db;
    private readonly ILogger<SearchExecutor> _logger;

    private const double ExactRankThreshold = 50.0;

    public SearchExecutor(Models.ApplicationDbContext db, ILogger<SearchExecutor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<SearchCandidate>> SearchAsync(ParsedQuery q, CancellationToken ct = default)
    {
        var hasCity = !string.IsNullOrWhiteSpace(q.Location);
        var hasStreet = !string.IsNullOrWhiteSpace(q.LocationText);

        // No city AND no street → return empty. The caller (Layer 6) decides
        // whether to ask the user for clarification or run a global search.
        if (!hasCity && !hasStreet) return new List<SearchCandidate>();

        // Build the base set: approved listings, optionally filtered by city,
        // listing type, property type, bedrooms (hard).
        var listings = _db.Listings
            .AsNoTracking()
            .Where(l => l.IsApproved)
            .Include(l => l.Address)
            .AsQueryable();

        if (q.ListingType is "buy")  listings = listings.Where(l => l.ListingType == ListingType.ForSale);
        if (q.ListingType is "rent") listings = listings.Where(l => l.ListingType == ListingType.ForRent);

        if (!string.IsNullOrEmpty(q.PropertyType))
        {
            var ptype = q.PropertyType.ToLowerInvariant() switch
            {
                "apartment" => PropertyType.Apartment,
                "villa"     => PropertyType.Villa,
                "studio"    => PropertyType.Studio,
                "duplex"    => PropertyType.Duplex,
                "penthouse" => PropertyType.Penthouse,
                _           => (PropertyType?)null,
            };
            if (ptype is not null) listings = listings.Where(l => l.PropertyType == ptype);
        }

        // Explicit bedrooms = hard filter. suggested_bedrooms is NOT.
        if (q.Bedrooms is int beds) listings = listings.Where(l => l.Bedrooms == beds);
        // Bathrooms uses ">= N" (same as the regular Filter endpoint) — exact
        // match here would too aggressively shrink results on a small dataset
        // where a 3-bath listing should still answer "I want 2 bathrooms".
        if (q.Bathrooms is int baths) listings = listings.Where(l => l.Bathrooms >= baths);

        // Price / area — apply both bounds when present.
        if (q.PriceMin is decimal pmin) listings = listings.Where(l => l.Price >= pmin);
        if (q.PriceMax is decimal pmax) listings = listings.Where(l => l.Price <= pmax);
        if (q.AreaMin  is decimal amin) listings = listings.Where(l => l.AreaSize >= amin);

        // Finishing — Listing.Finishing stores the same lowercase key
        // (fully_finished / semi_finished / core_shell / furnished / unfurnished)
        // that the prompt now emits. Plausibility already dropped invalid values.
        if (!string.IsNullOrWhiteSpace(q.FinishingLevel))
        {
            var f = q.FinishingLevel;
            listings = listings.Where(l => l.Finishing == f);
        }

        // Completion status — nullable on Listing; only filter when the user said.
        if (!string.IsNullOrWhiteSpace(q.CompletionStatus)
            && Enum.TryParse<CompletionStatus>(q.CompletionStatus, ignoreCase: true, out var cs))
        {
            listings = listings.Where(l => l.CompletionStatus == cs);
        }

        // Payment method — "Both" listings satisfy a Cash or Installments filter
        // (a seller who accepts both still accepts each one). Mirrors the rule
        // ListingController.Filter uses on the regular filter endpoint.
        if (!string.IsNullOrWhiteSpace(q.PaymentMethod))
        {
            if (q.PaymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                listings = listings.Where(l => l.PaymentMethod == PaymentMethod.Cash || l.PaymentMethod == PaymentMethod.Both);
            else if (q.PaymentMethod.Equals("Installment", StringComparison.OrdinalIgnoreCase))
                listings = listings.Where(l => l.PaymentMethod == PaymentMethod.Installments || l.PaymentMethod == PaymentMethod.Both);
            // "Both" → user said they're flexible; don't filter at all.
        }

        // Amenities — Listing.Amenities is a JSON array string (e.g. ["elevator","balcony"]).
        // Match the quoted key so "pool" doesn't accidentally hit "shared_pool".
        // Listing must have ALL requested amenities (AND semantics), same as the
        // regular filter endpoint.
        if (q.Amenities is { Count: > 0 } wanted)
        {
            foreach (var key in wanted)
            {
                var needle = "\"" + key + "\"";
                listings = listings.Where(l => l.Amenities != null && l.Amenities.Contains(needle));
            }
        }

        if (hasCity)
        {
            // Same case-and-hyphen-insensitive logic the existing Filter
            // endpoint uses. Match against City OR Region.
            var cityLc = q.Location!.ToLower().Replace("-", " ").Trim();
            listings = listings.Where(l => l.Address != null && (
                (l.Address.City   != null && l.Address.City  .ToLower().Replace("-", " ").Trim() == cityLc) ||
                (l.Address.Region != null && l.Address.Region.ToLower().Replace("-", " ").Trim() == cityLc)));
        }

        // Without a street fragment, we're done — every row is MatchQuality.None.
        if (!hasStreet)
        {
            return await listings
                .Select(l => new SearchCandidate
                {
                    ListingId = l.Id,
                    Match     = MatchQuality.None,
                    FtsRank   = 0,
                    Bedrooms  = l.Bedrooms,
                    Price     = l.Price,
                    ViewCount = l.ViewCount,
                    City      = l.Address!.City,
                    Region    = l.Address!.Region,
                })
                .ToListAsync(ct);
        }

        // FTS path: get FTS hits via CONTAINSTABLE, then LEFT JOIN with the
        // filtered listings so non-FTS-hit rows in the same city still come
        // back as MatchQuality.None (the fallback set).
        var streetKey = StreetNormalizer.Normalize(q.LocationText!);
        var keywords  = BuildContainsExpression(streetKey);

        // Pull the candidate listing IDs out of the EF query first so we can
        // pass them by parameter to the raw SQL.
        var candidateIds = await listings.Select(l => l.Id).ToListAsync(ct);

        if (candidateIds.Count == 0) return new List<SearchCandidate>();

        // Use raw SQL for CONTAINSTABLE — EF Core has no LINQ for it.
        // KEY column from CONTAINSTABLE is the address PK; we project it
        // up to ListingId via the address's ListingId FK.
        var sql = $@"
SELECT a.ListingId,
       ct.[RANK] AS FtsRank
FROM CONTAINSTABLE(dbo.Addresses, StreetSearchKey, {{0}}) ct
INNER JOIN dbo.Addresses a ON a.Id = ct.[KEY]
WHERE a.ListingId IN ({string.Join(",", candidateIds.Select((_, i) => $"{{{i + 1}}}"))})";

        var args = new object[candidateIds.Count + 1];
        args[0] = keywords;
        for (var i = 0; i < candidateIds.Count; i++) args[i + 1] = candidateIds[i];

        var ftsHits = await _db.Database
            .SqlQueryRaw<FtsHit>(sql, args)
            .ToListAsync(ct);

        // Promote int → double here so the rest of the pipeline (which expects
        // FtsRank: double) keeps a single numeric type.
        var ftsByListing = ftsHits.ToDictionary(h => h.ListingId, h => (double)h.FtsRank);

        // Now project the full candidate list, attaching FTS rank where present.
        var enriched = await listings
            .Select(l => new
            {
                l.Id, l.Bedrooms, l.Price, l.ViewCount,
                City = l.Address!.City, Region = l.Address!.Region,
            })
            .ToListAsync(ct);

        return enriched.Select(l => new SearchCandidate
        {
            ListingId = l.Id,
            Match     = ClassifyMatch(ftsByListing.TryGetValue(l.Id, out var r) ? r : 0),
            FtsRank   = ftsByListing.TryGetValue(l.Id, out var rr) ? rr : 0,
            Bedrooms  = l.Bedrooms,
            Price     = l.Price,
            ViewCount = l.ViewCount,
            City      = l.City,
            Region    = l.Region,
        }).ToList();
    }

    private static MatchQuality ClassifyMatch(double rank)
    {
        if (rank == 0)                        return MatchQuality.None;
        if (rank >= ExactRankThreshold)       return MatchQuality.Exact;
        return MatchQuality.Partial;
    }

    // Build a CONTAINS expression that handles each token as a prefix-OR.
    // Tokens are space-separated and we don't trust user input enough to
    // pass it raw into CONTAINS (where double-quotes are syntactic).
    //
    // Token length: we keep single-digit tokens like "9" because Egyptian
    // streets are routinely numbered ("شارع 9"); dropping them would make
    // street search miss its most common case. We do drop bare 1-letter
    // alphabetic tokens that are usually noise (a stray "i", "k", etc.).
    private static string BuildContainsExpression(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2 || (t.Length == 1 && char.IsDigit(t[0])))
            .Select(EscapeToken)
            .ToArray();
        if (tokens.Length == 0) return "\"\"";   // forces zero matches; safer than throwing
        return string.Join(" OR ", tokens.Select(t => $"\"{t}*\""));
    }

    // CONTAINS treats " as a string delimiter; embedded quotes break the parse.
    private static string EscapeToken(string token) => token.Replace("\"", "");

    // SQL Server's CONTAINSTABLE [RANK] is INT — match the type exactly,
    // otherwise EF Core's value reader does GetDouble() on an Int32 and
    // throws InvalidCastException at the first FTS hit (graduation-day bug
    // territory). We promote it to double for ranking use after read.
    private class FtsHit
    {
        public Guid ListingId { get; set; }
        public int  FtsRank   { get; set; }
    }
}
