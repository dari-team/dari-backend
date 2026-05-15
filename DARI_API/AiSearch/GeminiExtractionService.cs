using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DARI_API.AiSearch;

// Layer 2 — Gemini implementation of IAiExtractionService.
//
// Why Gemini instead of Llama-on-Groq:
//   Llama-class models on Groq deliver strong English/mixed-script results
//   but inconsistent Egyptian-Arabic NER (silently dropping price keywords,
//   payment, and city extraction). Gemini 2.5 Flash with native JSON Schema
//   mode reliably extracts these fields. The trade-off: slightly higher
//   latency (1-2s vs ~600ms) and a smaller free tier (250–500 RPD vs ~1000),
//   both acceptable given the 3/week per-user quota.
//
// Failure handling — same contract as the Groq service:
//   - Transport / 5xx error -> retry once
//   - 4xx response          -> throw immediately (no retry)
//   - Unparseable JSON      -> retry once
//   - All failures bubble up as AiExtractionException
//
// Gemini's "responseSchema" + "responseMimeType: application/json" forces the
// model to emit JSON conforming to the schema we provide — eliminating the
// "valid JSON, wrong shape" failure mode that's common with prompt-only
// approaches. This is materially stronger than Groq's response_format.
public class GeminiExtractionService : IAiExtractionService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiExtractionService> _logger;

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public GeminiExtractionService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GeminiExtractionService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<AiExtractionResult> ExtractAsync(string userQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            throw new AiExtractionException("Query is empty.");

        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiExtractionException("Gemini API key is not configured.");

        var model    = _config["Gemini:Model"]   ?? "gemini-3.1-flash-lite-preview";
        var baseUrl  = (_config["Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
        // Gemini's REST endpoint takes the API key as a query string parameter.
        var endpoint = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";

        // Gemini's request shape: contents[] with parts[], plus a generationConfig
        // that specifies JSON output and the response schema. The system prompt
        // goes in `systemInstruction`.
        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = AiSearchPrompt.SystemPrompt } },
            },
            contents = new[]
            {
                new
                {
                    role  = "user",
                    parts = new[] { new { text = userQuery } },
                },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema   = ParsedQuerySchema,
                temperature      = AiSearchPrompt.Temperature,
                maxOutputTokens  = 1024,
                // thinkingConfig disables Gemini 2.5's reasoning trace so the
                // full token budget is available for the JSON output.
                thinkingConfig   = new { thinkingBudget = 0 },
            },
        };

        var sw = Stopwatch.StartNew();
        ParsedQuery? parsed = null;
        string content = "";
        int? promptTokens = null, completionTokens = null;
        bool retryUsed = false;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var (raw, usage) = await CallGeminiAsync(endpoint, requestBody, ct);
                content = raw;
                promptTokens     = usage.PromptTokens;
                completionTokens = usage.CompletionTokens;
                parsed = JsonSerializer.Deserialize<ParsedQuery>(content, SnakeCaseOptions)
                         ?? throw new AiExtractionException("Gemini returned null JSON object.");
                break;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt == 1)
            {
                retryUsed = true;
                _logger.LogWarning(ex,
                    "Gemini extraction attempt {Attempt} failed (retryable): {Message}",
                    attempt, ex.Message);
            }
            catch (JsonException ex) when (attempt == 1)
            {
                retryUsed = true;
                _logger.LogWarning(ex,
                    "Gemini returned unparseable JSON on attempt 1; retrying");
            }
            catch (Exception ex)
            {
                if (ex is AiExtractionException) throw;
                throw new AiExtractionException(
                    "Gemini extraction failed: " + ex.Message, ex);
            }
        }

        if (parsed is null)
            throw new AiExtractionException("Gemini extraction failed after retry.");

        sw.Stop();

        return new AiExtractionResult
        {
            Parsed             = parsed,
            Language           = DetectLanguage(userQuery),
            RawJson            = content,
            LatencyMs          = sw.ElapsedMilliseconds,
            PromptTokens       = promptTokens,
            CompletionTokens   = completionTokens,
            RetryUsed          = retryUsed,
            ModelUsed          = model,
        };
    }

    private async Task<(string Json, TokenUsage Usage)> CallGeminiAsync(
        string endpoint, object body, CancellationToken ct)
    {
        // We register an "Gemini" named HttpClient in DI; if missing, fall back
        // to a default client so this service is also usable in tests that
        // don't wire the named client.
        var client = _httpFactory.CreateClient("Gemini");
        if (client.Timeout == Timeout.InfiniteTimeSpan || client.Timeout > TimeSpan.FromSeconds(30))
            client.Timeout = TimeSpan.FromSeconds(20);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };

        using var res = await client.SendAsync(req, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            // Gemini's 429 means our project hit the daily/per-minute API
            // budget — distinct from per-user 3/week quota. Surface as a
            // dedicated exception so the controller can return a friendly
            // "AI service at capacity" message instead of a generic 502.
            if ((int)res.StatusCode == 429)
                throw new AiProviderQuotaException(
                    $"Gemini quota exhausted (project-level): {Truncate(raw, 300)}");

            if ((int)res.StatusCode is >= 400 and < 500)
                throw new AiExtractionException(
                    $"Gemini returned {(int)res.StatusCode}: {Truncate(raw, 300)}");

            throw new HttpRequestException(
                $"Gemini {(int)res.StatusCode}: {Truncate(raw, 300)}",
                inner: null,
                statusCode: res.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(raw, SnakeCaseOptions)
            ?? throw new AiExtractionException("Gemini response was empty.");

        var content = parsed.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .FirstOrDefault()?
            .Text
            ?? throw new AiExtractionException("Gemini response missing candidates[].content.parts[].text.");

        var usage = new TokenUsage
        {
            PromptTokens     = parsed.UsageMetadata?.PromptTokenCount,
            CompletionTokens = parsed.UsageMetadata?.CandidatesTokenCount,
        };

        return (content, usage);
    }

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException;

    private static string DetectLanguage(string text)
    {
        var hasArabic = text.Any(c => c >= 0x0600 && c <= 0x06FF);
        var hasLatin  = text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        return (hasArabic, hasLatin) switch
        {
            (true, true)  => "mixed",
            (true, false) => "ar",
            _             => "en",
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    // ---- ParsedQuery JSON Schema (for Gemini's responseSchema) ----
    // Gemini supports OpenAPI-style schema. We declare every field as
    // nullable so the model can emit nulls when the user didn't say.
    private static object ParsedQuerySchema => new
    {
        type = "object",
        properties = new
        {
            property_type      = new { type = "string", nullable = true, @enum = new[] { "apartment", "villa", "studio", "duplex", "penthouse" } },
            listing_type       = new { type = "string", nullable = true, @enum = new[] { "buy", "rent" } },
            location           = new { type = "string", nullable = true },
            location_text      = new { type = "string", nullable = true },
            near_metro         = new { type = "boolean", nullable = true },
            price_min          = new { type = "number",  nullable = true },
            price_max          = new { type = "number",  nullable = true },
            bedrooms           = new { type = "integer", nullable = true },
            bathrooms          = new { type = "integer", nullable = true },
            suggested_bedrooms = new { type = "integer", nullable = true },
            area_min           = new { type = "number",  nullable = true },
            finishing_level    = new { type = "string", nullable = true, @enum = new[] { "fully_finished", "semi_finished", "core_shell", "furnished", "unfurnished" } },
            payment_method     = new { type = "string", nullable = true, @enum = new[] { "Cash", "Installment", "Both" } },
            max_down_payment   = new { type = "number",  nullable = true },
            completion_status  = new { type = "string", nullable = true, @enum = new[] { "Ready", "OffPlan" } },
            amenities          = new
            {
                type     = "array",
                nullable = true,
                items    = new
                {
                    type  = "string",
                    @enum = new[]
                    {
                        "elevator", "covered_parking", "natural_gas", "security",
                        "backup_generator", "utility_meters", "central_ac",
                        "built_in_wardrobes", "maids_room", "balcony", "private_roof",
                        "storage_room", "intercom", "internet", "within_compound",
                        "shared_pool", "shared_gym", "kids_play_area",
                        "landscaped_gardens", "private_garden", "private_pool",
                        "private_jacuzzi", "water_view", "landmark_view",
                        "pets_allowed",
                    },
                },
            },
        },
        required = new[]
        {
            "property_type", "listing_type", "location", "location_text",
            "near_metro", "price_min", "price_max", "bedrooms", "bathrooms",
            "suggested_bedrooms", "area_min", "finishing_level",
            "payment_method", "max_down_payment",
            "completion_status", "amenities",
        },
    };

    private class TokenUsage
    {
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
    }

    // ---- Gemini wire format ----
    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]    public List<GeminiCandidate>? Candidates { get; set; }
        [JsonPropertyName("usageMetadata")] public GeminiUsage? UsageMetadata { get; set; }
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
    private class GeminiUsage
    {
        [JsonPropertyName("promptTokenCount")]     public int? PromptTokenCount     { get; set; }
        [JsonPropertyName("candidatesTokenCount")] public int? CandidatesTokenCount { get; set; }
    }
}
