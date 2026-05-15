using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DARI_API.AiSearch;

// One Gemini call per listing creation. The lister types in one script;
// Gemini fills in the other. We hand back both so the controller can store
// them and build the FTS search key from their union.
//
// Failure policy: never throw to the caller. Listing creation must not fail
// because Gemini is slow / unavailable / quota-exhausted. On any non-success
// we return { Ok = false } and let ListingController fall back to the typed
// form alone. The row gets stamped "fallback-typed" so an admin job can
// re-key it later when Gemini is healthy again.
//
// Quota notes: Gemini 3.1 Flash Lite free tier is 500 RPD per project,
// shared with /api/AiSearch. Demo scale (~30 listing creations + ~50
// searches / day) is well under the cap. We do NOT retry on transport
// errors — listing creation should feel snappy, not stall on a flaky API.
public class GeminiStreetTransliterationService : IStreetTransliterationService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiStreetTransliterationService> _logger;

    private const string SystemPrompt = """
You transliterate Egyptian street/area names between Arabic and Latin.
Return ONLY JSON: {"ar":"<Arabic form>","latin":"<Latin form>"} — no prose.

RULES:
- The user's input is already correct in one script. Preserve it verbatim
  in the matching field, and produce the OTHER script's natural Egyptian
  spelling (not academic transliteration).
- "Abbas El Akkad" → {"ar":"عباس العقاد","latin":"Abbas El Akkad"}
- "عباس العقاد"   → {"ar":"عباس العقاد","latin":"Abbas El Akkad"}
- Latin form: use the spelling Egyptians actually write online, not Buckwalter.
- Drop generic prefixes the user includes ("شارع", "St.", "Street") from BOTH
  forms — they're noise for matching. Keep the proper-noun portion.
- Numbers stay verbatim in both forms ("شارع 90" → ar: "90", latin: "90").
- If the input is empty, garbage, or both scripts are mixed, return what you can.

Examples:
  "El Tagamoa El Khames"  → {"ar":"التجمع الخامس","latin":"El Tagamoa El Khames"}
  "المعادي"                → {"ar":"المعادي","latin":"El Maadi"}
  "Sheikh Zayed"          → {"ar":"الشيخ زايد","latin":"Sheikh Zayed"}
  "26th of July"          → {"ar":"26 يوليو","latin":"26th of July"}
""";

    public GeminiStreetTransliterationService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GeminiStreetTransliterationService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<StreetTransliteration> TransliterateAsync(string typed, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return new StreetTransliteration { Ok = false };

        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini transliteration skipped: API key not configured.");
            return new StreetTransliteration { Ok = false };
        }

        var model    = _config["Gemini:Model"]   ?? "gemini-3.1-flash-lite-preview";
        var baseUrl  = (_config["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        var endpoint = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";

        var requestBody = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = typed.Trim() } } },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema   = new
                {
                    type = "object",
                    properties = new
                    {
                        ar    = new { type = "string", nullable = true },
                        latin = new { type = "string", nullable = true },
                    },
                    required = new[] { "ar", "latin" },
                },
                temperature      = 0.0,
                maxOutputTokens  = 200,
                thinkingConfig   = new { thinkingBudget = 0 },
            },
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpFactory.CreateClient("Gemini");
            if (client.Timeout == Timeout.InfiniteTimeSpan || client.Timeout > TimeSpan.FromSeconds(10))
                client.Timeout = TimeSpan.FromSeconds(8);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"),
            };

            using var res = await client.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gemini transliteration HTTP {Status} in {Ms}ms — falling back to typed form.",
                    (int)res.StatusCode, sw.ElapsedMilliseconds);
                return new StreetTransliteration { Ok = false };
            }

            var envelope = JsonSerializer.Deserialize<GeminiResponse>(raw)
                ?? throw new InvalidOperationException("Gemini response was empty.");

            var content = envelope.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                ?? throw new InvalidOperationException("Gemini response missing text content.");

            var parsed = JsonSerializer.Deserialize<StreetPair>(content)
                ?? throw new InvalidOperationException("Gemini returned null JSON object.");

            var ar    = Trim(parsed.Ar);
            var latin = Trim(parsed.Latin);
            if (string.IsNullOrEmpty(ar) && string.IsNullOrEmpty(latin))
                return new StreetTransliteration { Ok = false };

            return new StreetTransliteration { Ar = ar, Latin = latin, Ok = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Gemini transliteration failed in {Ms}ms — falling back to typed form.",
                sw.ElapsedMilliseconds);
            return new StreetTransliteration { Ok = false };
        }
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- wire format (subset of Gemini's response shape) ----
    private class GeminiResponse
    {
        [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }
    }
    private class GeminiCandidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }
    private class GeminiContent
    {
        [JsonPropertyName("parts")] public List<GeminiPart>? Parts { get; set; }
    }
    private class GeminiPart
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
    private class StreetPair
    {
        [JsonPropertyName("ar")]    public string? Ar    { get; set; }
        [JsonPropertyName("latin")] public string? Latin { get; set; }
    }
}
