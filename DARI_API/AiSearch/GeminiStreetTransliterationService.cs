using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

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
You convert Egyptian street/area names between Arabic and English.
Return ONLY JSON: {"ar":"<Arabic form>","latin":"<English form>"} — no prose.

The "ar" field is the natural Arabic spelling.
The "latin" field is the ENGLISH NAME — translated, not transliterated —
when a real English name exists. Only fall back to Franco-Arabic
transliteration when the name has no English equivalent (e.g. personal-name
streets like "عباس العقاد"; people's names aren't translated).

TRANSLATE these — use the English name, not Franco:
  التجمع الخامس        → "5th Settlement"
  التجمع الأول          → "1st Settlement"
  المعادي                → "Maadi"
  مدينة نصر             → "Nasr City"
  الشيخ زايد            → "Sheikh Zayed"
  6 أكتوبر / السادس من أكتوبر → "6th of October"
  26 يوليو / السادس والعشرين من يوليو → "26th of July"
  مصر الجديدة           → "Heliopolis"
  الزمالك               → "Zamalek"
  المهندسين             → "Mohandessin"
  الدقي                 → "Dokki"
  وسط البلد             → "Downtown"
  الجيزة                → "Giza"
  القاهرة الجديدة        → "New Cairo"
  الإسكندرية            → "Alexandria"
  القاهرة               → "Cairo"
  كورنيش النيل          → "Nile Corniche"
  مدينة الرحاب          → "Rehab City"
  مدينة بدر             → "Badr City"

TRANSLITERATE (Franco-Arabic) only when no English name exists — typically
streets named after people, tribes, or specific Arabic words with no
direct translation:
  عباس العقاد           → "Abbas El Akkad"
  جمال عبد الناصر       → "Gamal Abdel Nasser"
  مكرم عبيد             → "Makram Ebeid"
  عمر بن الخطاب         → "Omar Ibn Al Khattab"

RULES:
- Preserve the user's input verbatim in the matching field — don't "fix"
  spelling. The other field is what you produce.
- Drop generic prefixes ("شارع", "St.", "Street", "ميدان", "Square") from
  BOTH forms — they're noise for matching. Keep only the proper-noun portion.
- Numbers stay verbatim ("شارع 90" → ar: "90", latin: "90").
- If a name combines a translatable district + a transliterated street
  (e.g. "التجمع الخامس - شارع التسعين"), apply each rule to its part:
  → {"ar":"التجمع الخامس - التسعين","latin":"5th Settlement - 90th Street"}.
- If the input is empty or completely opaque, return what you can with
  the other field as null.

Examples:
  Input "5th Settlement"          → {"ar":"التجمع الخامس","latin":"5th Settlement"}
  Input "التجمع الخامس"            → {"ar":"التجمع الخامس","latin":"5th Settlement"}
  Input "عباس العقاد"              → {"ar":"عباس العقاد","latin":"Abbas El Akkad"}
  Input "Abbas El Akkad"          → {"ar":"عباس العقاد","latin":"Abbas El Akkad"}
  Input "شارع التسعين، التجمع الخامس" → {"ar":"التسعين، التجمع الخامس","latin":"90th Street, 5th Settlement"}
  Input "الشيخ زايد"               → {"ar":"الشيخ زايد","latin":"Sheikh Zayed"}
  Input "26th of July"            → {"ar":"26 يوليو","latin":"26th of July"}
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
            // Bumped from 8s → 30s: the longer prompt (with district map +
            // examples) pushes Gemini occasionally over 10s on the free tier.
            if (client.Timeout == Timeout.InfiniteTimeSpan || client.Timeout > TimeSpan.FromSeconds(30))
                client.Timeout = TimeSpan.FromSeconds(30);

            // Use a relaxed encoder so the Arabic in the system prompt is sent
            // as raw UTF-8 instead of \uXXXX escapes. Same content either way,
            // but cuts the request body roughly in half and is easier for
            // server-side log inspection.
            var serializeOpts = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, serializeOpts),
                    Encoding.UTF8,
                    "application/json"),
            };

            using var res = await client.SendAsync(req, ct);
            var raw = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Gemini transliteration HTTP {Status} in {Ms}ms: {Body}",
                    (int)res.StatusCode, sw.ElapsedMilliseconds, Truncate(raw, 500));
                return new StreetTransliteration { Ok = false };
            }

            var envelope = JsonSerializer.Deserialize<GeminiResponse>(raw)
                ?? throw new InvalidOperationException("Gemini response was empty.");

            var content = envelope.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                ?? throw new InvalidOperationException(
                    "Gemini response missing text content. Raw=" + Truncate(raw, 500));

            var parsed = JsonSerializer.Deserialize<StreetPair>(content)
                ?? throw new InvalidOperationException(
                    "Gemini returned null JSON object. Content=" + Truncate(content, 500));

            var ar    = Trim(parsed.Ar);
            var latin = Trim(parsed.Latin);
            if (string.IsNullOrEmpty(ar) && string.IsNullOrEmpty(latin))
            {
                _logger.LogWarning(
                    "Gemini returned empty pair in {Ms}ms. Content={Content}",
                    sw.ElapsedMilliseconds, Truncate(content, 500));
                return new StreetTransliteration { Ok = false };
            }

            return new StreetTransliteration { Ar = ar, Latin = latin, Ok = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Gemini transliteration failed in {Ms}ms: {Type} {Message}",
                sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
            return new StreetTransliteration { Ok = false };
        }
    }

    private static string Truncate(string s, int max) =>
        s is null ? "" : (s.Length <= max ? s : s[..max] + "…");

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
