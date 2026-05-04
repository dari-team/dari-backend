using System.Text.Json;

namespace DARI_API.AiSearch;

// Layer 7 — request-scoped log record. Fire-and-forget from the controller
// (Task.Run + structured logging) so it never adds latency to the user's
// request. The four metric thresholds from the design log:
//
//   street_match=none rate    > 20% → romanization degrading
//   plausibility rejection    > 10% → AI model drift
//   AI provider fallback rate >  5% → primary-provider degradation (v2 metric)
//   zero-result rate          > 30% → inventory gap
//
// We don't compute aggregate metrics here — we just log the raw fields and
// let the host's log pipeline (or future analytics layer) compute them.
public class AiSearchLog
{
    public string RawQuery            { get; set; } = "";
    public string DetectedLanguage    { get; set; } = "";
    public ParsedQuery? AiOutput      { get; set; }
    public List<PlausibilityChange> PlausibilityChanges { get; set; } = new();
    public string StreetMatch         { get; set; } = "";
    public int ResultCount            { get; set; }
    public bool FallbackTriggered     { get; set; }
    public long LatencyAiMs           { get; set; }
    public long LatencyDbMs           { get; set; }
    public string ModelUsed           { get; set; } = "";
    public bool RetryUsed             { get; set; }
    public int? PromptTokens          { get; set; }
    public int? CompletionTokens      { get; set; }
    public DateTime TimestampUtc      { get; set; } = DateTime.UtcNow;
}

public interface IAiSearchLogger
{
    void LogAsync(AiSearchLog entry);
}

// Default impl: writes to ILogger as one structured JSON line per request.
// Easy to replace with a DB-backed sink (SQL Server, OpenTelemetry, etc.)
// when graduation prep wants real metrics.
public class AiSearchLogger : IAiSearchLogger
{
    private readonly ILogger<AiSearchLogger> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public AiSearchLogger(ILogger<AiSearchLogger> logger) => _logger = logger;

    public void LogAsync(AiSearchLog entry)
    {
        // Detach from request lifetime — the user's response shouldn't wait
        // on logger I/O.
        _ = Task.Run(() =>
        {
            try
            {
                var json = JsonSerializer.Serialize(entry, JsonOpts);
                _logger.LogInformation("ai_search_event {Event}", json);
            }
            catch (Exception ex)
            {
                // Logging must never throw user-visible errors; swallow.
                _logger.LogError(ex, "Failed to write ai_search_event");
            }
        });
    }
}
