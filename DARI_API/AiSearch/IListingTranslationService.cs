namespace DARI_API.AiSearch;

// Produces the bilingual (Arabic + English) versions of a listing's free-text
// fields — title and description. Called once at listing-create time so the
// listing page can render whichever language matches the viewer's UI.
//
// The lister writes in one language (Arabic or English); the model fills in the
// other and echoes back the original. Implementations must never throw for the
// missing/opaque-input case — they return whatever they have and let the caller
// fall back to the user's typed text.
public interface IListingTranslationService
{
    Task<ListingTranslation> TranslateAsync(string title, string description, CancellationToken ct = default);
}

public sealed class ListingTranslation
{
    public string? TitleAr { get; init; }
    public string? TitleEn { get; init; }
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }

    // True when the model returned usable content. False means the caller should
    // treat this as degraded and store nothing (the original Title/Description
    // remain the only copy, and the frontend falls back to them).
    public bool Ok { get; init; }
}
