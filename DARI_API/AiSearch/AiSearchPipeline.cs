using System.Diagnostics;
using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DARI_API.AiSearch;

// Orchestrates Layers 2–7. The controller is a thin shell that calls
// SearchAsync and serializes the result.
//
// Caching: AI extraction is cached by (query, language) hash for 5 minutes
// so demo refreshes don't burn API credits. Search results are NOT cached
// (listings change; ranking is cheap; staleness here would be embarrassing).
public class AiSearchPipeline
{
    private readonly IAiExtractionService _ai;
    private readonly SearchExecutor _executor;
    private readonly IAiSearchLogger _logger;
    private readonly IMemoryCache _cache;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AiSearchPipeline> _diagnosticLogger;

    private static readonly TimeSpan AiCacheLifetime = TimeSpan.FromMinutes(5);

    public AiSearchPipeline(
        IAiExtractionService ai,
        SearchExecutor executor,
        IAiSearchLogger logger,
        IMemoryCache cache,
        ApplicationDbContext db,
        ILogger<AiSearchPipeline> diagnosticLogger)
    {
        _ai = ai;
        _executor = executor;
        _logger = logger;
        _cache = cache;
        _db = db;
        _diagnosticLogger = diagnosticLogger;
    }

    public async Task<AiSearchResponse> SearchAsync(string userQuery, CancellationToken ct = default)
    {
        var trimmed = (userQuery ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Query is empty.");

        // ── Layer 2: AI extraction (cached) ────────────────────────────────
        var cacheKey = $"aisearch:extract:{trimmed.ToLowerInvariant()}";
        var aiResult = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AiCacheLifetime;
            return await _ai.ExtractAsync(trimmed, ct);
        });

        if (aiResult is null)
            throw new InvalidOperationException("AI extraction returned null unexpectedly.");

        // ── Layer 3: Plausibility ──────────────────────────────────────────
        var plausibility = PlausibilityChecker.Check(aiResult.Parsed);

        // ── Layer 4: Search execution ──────────────────────────────────────
        var dbSw = Stopwatch.StartNew();
        var candidates = await _executor.SearchAsync(plausibility.Cleaned, ct);
        dbSw.Stop();

        // ── Layer 5: Ranking ───────────────────────────────────────────────
        var ranked = Ranker.Rank(candidates, plausibility.Cleaned);

        // Top-quality match across the whole result set drives the meta flags.
        var bestMatch = ranked.FirstOrDefault()?.Match ?? MatchQuality.None;
        var streetMatch = bestMatch switch
        {
            MatchQuality.Exact   => "exact",
            MatchQuality.Partial => "partial",
            _                    => "none",
        };
        // Fallback notice fires only when the user mentioned a known city AND
        // a street fragment AND we couldn't match the street. Without a known
        // city the message "showing all listings in [city]" has nothing to
        // fill the placeholder — emitting it would be confusing UX.
        var fallbackApplied =
            !string.IsNullOrWhiteSpace(plausibility.Cleaned.LocationText)
            && !string.IsNullOrWhiteSpace(plausibility.Cleaned.Location)
            && bestMatch == MatchQuality.None;

        // ── Layer 6: Hydrate top results into ListingResponse ──────────────
        // We hydrate AFTER ranking so we don't pay the cost of loading
        // listings that the ranker would just push to the bottom.
        var topIds = ranked.Take(50).Select(c => c.ListingId).ToList();
        var listings = topIds.Count == 0 ? new List<Listing>()
            : await _db.Listings
                .AsNoTracking()
                .Include(l => l.Address)
                .Include(l => l.Images)
                .Where(l => topIds.Contains(l.Id))
                .ToListAsync(ct);

        // Preserve ranker order (EF's IN() returns DB order, not our order).
        var byId = listings.ToDictionary(l => l.Id);
        var ordered = topIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        // ── Layer 6 cont: response assembly ────────────────────────────────
        var response = new AiSearchResponse
        {
            Results = ordered.Select(ListingResponse.From).ToList(),
            Meta = new AiSearchMeta
            {
                StreetMatch       = streetMatch,
                FallbackApplied   = fallbackApplied,
                ResultCount       = ranked.Count,
                TierBreakdown     = new TierBreakdown
                {
                    Exact   = ranked.Count(c => c.Match == MatchQuality.Exact),
                    Partial = ranked.Count(c => c.Match == MatchQuality.Partial),
                    None    = ranked.Count(c => c.Match == MatchQuality.None),
                },
                ParsedFilters     = plausibility.Cleaned,
                Corrections       = plausibility.Changes,
                Language          = aiResult.Language,
                LatencyAiMs       = aiResult.LatencyMs,
                LatencyDbMs       = dbSw.ElapsedMilliseconds,
                RetryUsed         = aiResult.RetryUsed,
                Notice            = BuildNotice(streetMatch, fallbackApplied, ranked.Count,
                                                plausibility.Cleaned, aiResult.Language),
            },
        };

        // ── Layer 7: async logging (fire-and-forget) ──────────────────────
        _logger.LogAsync(new AiSearchLog
        {
            RawQuery            = trimmed,
            DetectedLanguage    = aiResult.Language,
            AiOutput            = aiResult.Parsed,
            PlausibilityChanges = plausibility.Changes,
            StreetMatch         = streetMatch,
            ResultCount         = ranked.Count,
            FallbackTriggered   = fallbackApplied,
            LatencyAiMs         = aiResult.LatencyMs,
            LatencyDbMs         = dbSw.ElapsedMilliseconds,
            ModelUsed           = aiResult.ModelUsed,
            RetryUsed           = aiResult.RetryUsed,
            PromptTokens        = aiResult.PromptTokens,
            CompletionTokens    = aiResult.CompletionTokens,
        });

        return response;
    }

    // Bilingual notices for Problem 8 — never hide why a search returned what it did.
    // Three cases, in priority order:
    //   1. Zero results — explain which hard filters were applied (this is the
    //      gap that left users staring at "No results" with no recourse).
    //   2. Street fallback — the city-fallback message from the original spec.
    //   3. Otherwise null (results match cleanly; no notice needed).
    private static string? BuildNotice(string streetMatch, bool fallbackApplied,
                                       int resultCount, ParsedQuery q, string lang)
    {
        var isAr = lang == "ar";

        if (resultCount == 0)
        {
            // Build a short list of the active hard filters so the user knows
            // what to relax. Bedrooms is the most common culprit (exact match,
            // per Problem 6); city + property type also gate hard.
            var bits = new List<string>();
            if (q.Bedrooms is int beds)         bits.Add(isAr ? $"{beds} غرف بالضبط" : $"exactly {beds} bedrooms");
            if (!string.IsNullOrWhiteSpace(q.PropertyType)) bits.Add(q.PropertyType!);
            if (!string.IsNullOrWhiteSpace(q.Location))     bits.Add(q.Location!);
            if (q.PriceMax is decimal pmax)     bits.Add(isAr ? $"≤ {pmax:N0}" : $"≤ {pmax:N0}");
            var summary = bits.Count > 0 ? string.Join(isAr ? "، " : ", ", bits) : (isAr ? "هذه الفلاتر" : "these filters");
            return isAr
                ? $"لم نجد عقارات تطابق ({summary}). جرب توسيع البحث (مثلاً تعديل عدد الغرف أو السعر)."
                : $"No listings match ({summary}). Try widening your search — e.g. relax the bedroom count or price.";
        }

        if (fallbackApplied)
        {
            var cityName = q.Location ?? (isAr ? "هذه المدينة" : "this city");
            return isAr
                ? $"لم نجد نتائج في هذا الشارع — عرض كل نتائج {cityName}"
                : $"No listings found on this street — showing all listings in {cityName}";
        }

        return null;
    }
}
