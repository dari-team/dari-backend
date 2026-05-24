using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace DARI_API.AiSearch;

// One Gemini call per listing creation. The lister writes the title +
// description in one language; Gemini returns both the Arabic and the English
// versions so the controller can store them and the listing page can render
// whichever the viewer's language calls for.
//
// Failure policy: never throw to the caller. Listing creation must not fail
// because Gemini is slow / unavailable / quota-exhausted. On any non-success
// we return { Ok = false } and let ListingController store nothing extra — the
// original Title/Description stay as the only copy and the frontend falls back
// to them. (Mirrors GeminiStreetTransliterationService's contract.)
//
// Quota notes: Gemini free tier is 500 RPD per project, shared with
// /api/AiSearch and street transliteration. Demo scale is well under the cap.
// We do NOT retry on transport errors — listing creation should feel snappy.
public class GeminiListingTranslationService : IListingTranslationService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiListingTranslationService> _logger;

    private const string SystemPrompt = """
You translate Egyptian real-estate listings between Arabic and English.
The user sends a property TITLE and DESCRIPTION written in EITHER Arabic or
English. Detect the language, then return BOTH language versions of each field.

Return ONLY JSON, no prose:
{"titleAr":"...","titleEn":"...","descriptionAr":"...","descriptionEn":"..."}

RULES:
- Echo the original text verbatim into its own language field (don't "fix" or
  paraphrase the side the user already wrote), and translate it into the other.
- Produce a natural, fluent translation — not word-for-word. Keep the tone of a
  real-estate ad.
- Preserve numbers, measurements, and prices exactly (e.g. "120 م²" ↔ "120 m²").
- Keep Egyptian place names in their conventional form per language
  (e.g. "التجمع الخامس" ↔ "5th Settlement", "المعادي" ↔ "Maadi").
- Do not add information that isn't in the source. Do not add contact details.
- If a field is empty, return empty strings for both of its language versions.
""";

    public GeminiListingTranslationService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GeminiListingTranslationService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<ListingTranslation> TranslateAsync(string title, string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            return new ListingTranslation { Ok = false };

        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Gemini listing translation skipped: API key not configured.");
            return new ListingTranslation { Ok = false };
        }

        var model    = _config["Gemini:Model"]   ?? "gemini-3.1-flash-lite-preview";
        var baseUrl  = (_config["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        var endpoint = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";

        // The model needs both fields labelled so it can place each translation.
        var userText = $"TITLE:\n{title?.Trim()}\n\nDESCRIPTION:\n{description?.Trim()}";

        var requestBody = new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userText } } },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema   = new
                {
                    type = "object",
                    properties = new
                    {
                        titleAr       = new { type = "string", nullable = true },
                        titleEn       = new { type = "string", nullable = true },
                        descriptionAr = new { type = "string", nullable = true },
                        descriptionEn = new { type = "string", nullable = true },
                    },
                    required = new[] { "titleAr", "titleEn", "descriptionAr", "descriptionEn" },
                },
                temperature     = 0.0,
                // Descriptions can be long; allow room for two full copies.
                maxOutputTokens = 2048,
                thinkingConfig  = new { thinkingBudget = 0 },
            },
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpFactory.CreateClient("Gemini");
            // Descriptions are longer than street names — give the call headroom.
            if (client.Timeout == Timeout.InfiniteTimeSpan || client.Timeout > TimeSpan.FromSeconds(45))
                client.Timeout = TimeSpan.FromSeconds(45);

            // Relaxed encoder so Arabic in the prompt/body is sent as raw UTF-8
            // instead of \uXXXX escapes — same content, smaller body.
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
                    "Gemini listing translation HTTP {Status} in {Ms}ms: {Body}",
                    (int)res.StatusCode, sw.ElapsedMilliseconds, Truncate(raw, 500));
                return new ListingTranslation { Ok = false };
            }

            var envelope = JsonSerializer.Deserialize<GeminiResponse>(raw)
                ?? throw new InvalidOperationException("Gemini response was empty.");

            var content = envelope.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                ?? throw new InvalidOperationException(
                    "Gemini response missing text content. Raw=" + Truncate(raw, 500));

            var parsed = JsonSerializer.Deserialize<TranslationPair>(content)
                ?? throw new InvalidOperationException(
                    "Gemini returned null JSON object. Content=" + Truncate(content, 500));

            var titleAr = Trim(parsed.TitleAr);
            var titleEn = Trim(parsed.TitleEn);
            var descAr  = Trim(parsed.DescriptionAr);
            var descEn  = Trim(parsed.DescriptionEn);

            // Need at least one usable field, otherwise treat as degraded.
            if (string.IsNullOrEmpty(titleAr) && string.IsNullOrEmpty(titleEn)
                && string.IsNullOrEmpty(descAr) && string.IsNullOrEmpty(descEn))
            {
                _logger.LogWarning(
                    "Gemini listing translation returned empty result in {Ms}ms. Content={Content}",
                    sw.ElapsedMilliseconds, Truncate(content, 500));
                return new ListingTranslation { Ok = false };
            }

            return new ListingTranslation
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                DescriptionAr = descAr,
                DescriptionEn = descEn,
                Ok = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Gemini listing translation failed in {Ms}ms: {Type} {Message}",
                sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
            return new ListingTranslation { Ok = false };
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
    private class TranslationPair
    {
        [JsonPropertyName("titleAr")]       public string? TitleAr { get; set; }
        [JsonPropertyName("titleEn")]       public string? TitleEn { get; set; }
        [JsonPropertyName("descriptionAr")] public string? DescriptionAr { get; set; }
        [JsonPropertyName("descriptionEn")] public string? DescriptionEn { get; set; }
    }
}
