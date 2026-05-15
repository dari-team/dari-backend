namespace DARI_API.AiSearch;

// Audit record for one plausibility-layer correction. Logged so Layer 7 can
// surface drift metrics ("rejection rate above 10% signals AI model drift").
public class PlausibilityChange
{
    public required string Field         { get; set; }
    public required string ReasonCode    { get; set; }
    public string? OriginalValue         { get; set; }
    public string? NewValue              { get; set; }
}

public class PlausibilityResult
{
    public required ParsedQuery Cleaned         { get; set; }
    public required List<PlausibilityChange> Changes = new();
}

public static class PlausibilityReasons
{
    public const string CityUnknown        = "CITY_UNKNOWN";
    public const string BedroomsAbsurd     = "BEDROOMS_ABSURD";
    public const string BedroomsConflict   = "BEDROOMS_CONFLICT";
    public const string PriceInverted      = "PRICE_INVERTED";
    public const string StreetEmpty        = "STREET_EMPTY";
    public const string FinishingUnknown   = "FINISHING_UNKNOWN";
    public const string PaymentUnknown     = "PAYMENT_UNKNOWN";
    public const string PropertyUnknown    = "PROPERTY_UNKNOWN";
    public const string PriceNegative      = "PRICE_NEGATIVE";
    public const string BathroomsAbsurd    = "BATHROOMS_ABSURD";
    public const string CompletionUnknown  = "COMPLETION_UNKNOWN";
    public const string AmenitiesUnknown   = "AMENITIES_UNKNOWN";
}
