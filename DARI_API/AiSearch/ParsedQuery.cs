namespace DARI_API.AiSearch;

// Output of Layer 2 — AI extraction. Every field is nullable: a missing
// signal must mean "user didn't say" so Layer 3 (plausibility) and Layer 4
// (search) can decide what to do. Never default to a wrong value.
//
// `LocationText` is the RAW street fragment exactly as the user typed it
// (Arabic or Latin) — it's what gets fed to StreetNormalizer for FTS.
// `Location` is normalized to a canonical English city name from the
// known-cities map.
public class ParsedQuery
{
    public string? PropertyType        { get; set; }   // apartment | villa | studio | duplex | penthouse
    public string? ListingType         { get; set; }   // buy | rent
    public string? Location            { get; set; }   // canonical English city ("Nasr City", "Maadi", ...)
    public string? LocationText        { get; set; }   // raw street/area fragment ("عباس العقاد")
    public bool?   NearMetro           { get; set; }
    public decimal? PriceMin           { get; set; }
    public decimal? PriceMax           { get; set; }
    public int?    Bedrooms            { get; set; }   // explicit → hard filter in Layer 4
    public int?    Bathrooms           { get; set; }
    public int?    SuggestedBedrooms   { get; set; }   // family-size → soft signal only
    public decimal? AreaMin            { get; set; }
    public string? FinishingLevel      { get; set; }   // fully_finished | semi_finished | core_shell | furnished | unfurnished
    public string? PaymentMethod       { get; set; }   // Cash | Installment | Both
    public decimal? MaxDownPayment     { get; set; }
    public string? CompletionStatus    { get; set; }   // Ready | OffPlan
    public List<string>? Amenities     { get; set; }   // canonical keys from frontend AMENITIES list
}

// Wrapper carrying observability data alongside the parsed payload.
// Used by Layer 7 (logging) and shows up in the API response so the
// frontend can render the AI summary and latency.
public class AiExtractionResult
{
    public required ParsedQuery Parsed   { get; set; }
    public required string Language      { get; set; }       // "ar" | "en" | "mixed"
    public required string RawJson       { get; set; }       // full AI output for logging
    public long LatencyMs                { get; set; }
    public int? PromptTokens             { get; set; }
    public int? CompletionTokens         { get; set; }
    public bool RetryUsed                { get; set; }
    public string ModelUsed              { get; set; } = "";
}

public class AiExtractionException : Exception
{
    public AiExtractionException(string message, Exception? inner = null) : base(message, inner) { }
}

// Specific exception for "the AI provider's quota is exhausted, not the user's".
// Distinct from the per-user 3/week quota — this happens when Gemini itself
// returns 429 because the project's daily request budget hit its ceiling.
// The controller surfaces this as 503 with code AI_SERVICE_QUOTA so the
// frontend can show a different message ("AI is at capacity today" rather
// than "your weekly limit is reached").
public class AiProviderQuotaException : AiExtractionException
{
    public AiProviderQuotaException(string message, Exception? inner = null) : base(message, inner) { }
}
