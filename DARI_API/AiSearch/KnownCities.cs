namespace DARI_API.AiSearch;

// Mirror of the frontend's FLAT_LOCATIONS (egyptLocations.ts). Used by the
// plausibility layer to validate AI-produced city names. Single source of
// truth lives on the frontend (the user-facing dropdown); this is a
// canonical-English-only echo for backend validation.
//
// Match is case-insensitive and treats hyphens/spaces as equivalent
// ("Nasr City" == "nasr-city" == "NASR CITY").
public static class KnownCities
{
    private static readonly HashSet<string> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cairo governorate
        "Cairo", "New Cairo", "Fifth Settlement", "Nasr City", "Heliopolis",
        "Maadi", "Zamalek", "Downtown", "Rehab City", "Shorouk City",
        "Badr City", "Ain Shams", "Shubra", "Tura", "Katameya",
        "New Administrative Capital", "Obour City", "First Settlement",
        // Giza
        "Giza", "Sheikh Zayed", "6th of October", "Dokki", "Mohandessin",
        "Agouza", "Haram", "Faisal", "Imbaba",
        // Alexandria
        "Alexandria", "Smouha", "Sidi Gaber", "Stanley", "Miami", "Montaza",
        "Borg El Arab",
        // Coastal / resort
        "North Coast", "Ain Sokhna", "Hurghada", "Sharm El Sheikh", "Marsa Alam",
        "Dahab",
        // Other governorates
        "Mansoura", "Tanta", "Zagazig", "Ismailia", "Suez", "Port Said",
        "Damanhour", "Banha", "Fayoum", "Beni Suef", "Minya", "Asyut", "Sohag",
        "Qena", "Luxor", "Aswan",
    };

    public static bool IsKnown(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return false;
        var norm = Normalize(city);
        return Canonical.Any(c => Normalize(c) == norm);
    }

    public static string? Canonicalize(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;
        var norm = Normalize(city);
        return Canonical.FirstOrDefault(c => Normalize(c) == norm);
    }

    private static string Normalize(string s) =>
        s.Trim().ToLowerInvariant().Replace('-', ' ');
}
