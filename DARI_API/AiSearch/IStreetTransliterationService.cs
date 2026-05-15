namespace DARI_API.AiSearch;

// Produces the bilingual ({ar, latin}) canonical pair for an address line.
// Called once at listing-create time so the UI can render whichever script
// matches the user's selected language. Implementations should never throw
// for missing-form cases — they return whatever they have and let the caller
// decide whether to fall back to the user's typed form.
public interface IStreetTransliterationService
{
    Task<StreetTransliteration> TransliterateAsync(string typed, CancellationToken ct = default);
}

public sealed class StreetTransliteration
{
    public string? Ar    { get; init; }
    public string? Latin { get; init; }
    // True when the model returned both forms successfully. False means the
    // caller should treat this as a degraded result and store whatever it can.
    public bool   Ok    { get; init; }
}
