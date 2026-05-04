using DARI_API.ViewModels;

namespace DARI_API.AiSearch;

// Layer 6 — final response shape sent to the frontend.
public class AiSearchResponse
{
    public required List<ListingResponse> Results { get; set; } = new();
    public required AiSearchMeta Meta             { get; set; }
}

public class AiSearchMeta
{
    public required string StreetMatch                { get; set; }   // "exact" | "partial" | "none"
    public required bool   FallbackApplied            { get; set; }   // true when no street match
    public required int    ResultCount               { get; set; }
    public required TierBreakdown TierBreakdown      { get; set; }
    public required ParsedQuery ParsedFilters        { get; set; }   // shown to user; lets them edit
    public required List<PlausibilityChange> Corrections { get; set; } = new();
    public required string Language                  { get; set; }   // "ar" | "en" | "mixed"
    public long LatencyAiMs                          { get; set; }
    public long LatencyDbMs                          { get; set; }
    public bool RetryUsed                            { get; set; }
    public string? Notice                            { get; set; }   // localized fallback notice
}

public class TierBreakdown
{
    public int Exact   { get; set; }
    public int Partial { get; set; }
    public int None    { get; set; }
}
