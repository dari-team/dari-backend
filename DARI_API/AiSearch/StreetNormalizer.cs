using System.Globalization;
using System.Text;

namespace DARI_API.AiSearch;

// Layer 1 of the AI search pipeline: deterministic street normalization.
//
// Both stored streets (at INSERT time) and incoming queries (at SEARCH time)
// pass through the SAME function so they collide. The output is a search key,
// not a display string — readability is not a goal, consistency is.
//
// Steps:
//   1. Strip Arabic diacritics (tashkeel)
//   2. Buckwalter-transliterate Arabic letters → Latin
//   3. Lowercase
//   4. Strip punctuation and connectors
//   5. Drop articles and common words ("el", "al", "the", "st", "شارع", ...)
//   6. Collapse whitespace
//
// Cross-language limitation: Latin input ("Abbas El Akkad") will not match
// Arabic input ("عباس العقاد") because we don't reverse-transliterate Latin
// → Arabic phonetics (ambiguous without ML). Arabic↔Arabic and Latin↔Latin
// matching both work cleanly. This is the documented v1 limitation.
//
// The version stamp is stored on every normalized row so a future v2 can
// re-normalize old data via a one-shot migration without breaking searches
// in flight.
public static class StreetNormalizer
{
    public const string Version = "buckwalter-v1";

    // Buckwalter transliteration map. Reference: Tim Buckwalter's standard.
    // We use the lowercased ASCII variant (no special chars like $, *, &) so
    // the search key is filesystem-and-URL-safe and case-insensitive.
    private static readonly Dictionary<char, string> ArabicToLatin = new()
    {
        // Letters
        ['ا'] = "a",  ['أ'] = "a",  ['إ'] = "a",  ['آ'] = "a",  ['ٱ'] = "a",
        ['ب'] = "b",
        ['ت'] = "t",  ['ة'] = "t",                       // taa marbuta → t
        ['ث'] = "th",
        ['ج'] = "j",
        ['ح'] = "h",
        ['خ'] = "kh",
        ['د'] = "d",
        ['ذ'] = "th",                                    // dhal also → th (collisions OK)
        ['ر'] = "r",
        ['ز'] = "z",
        ['س'] = "s",
        ['ش'] = "sh",
        ['ص'] = "s",
        ['ض'] = "d",
        ['ط'] = "t",
        ['ظ'] = "z",
        ['ع'] = "a",                                     // ayn → a (matches Egyptian "Abbas")
        ['غ'] = "gh",
        ['ف'] = "f",
        ['ق'] = "q",
        ['ك'] = "k",
        ['ل'] = "l",
        ['م'] = "m",
        ['ن'] = "n",
        ['ه'] = "h",
        ['و'] = "w",
        ['ي'] = "y",  ['ى'] = "y",  ['ئ'] = "y",
        ['ء'] = "",   ['ؤ'] = "w",
        // Egyptian-specific
        ['پ'] = "p",
        ['چ'] = "g",                                     // Egyptian gym
        ['ڤ'] = "v",
        ['گ'] = "g",
        // Tatweel — visual stretch, no sound
        ['ـ'] = "",
    };

    // Tashkeel / Quranic marks — strip entirely.
    private static readonly HashSet<char> Diacritics = new()
    {
        'ً', 'ٌ', 'ٍ', 'َ', 'ُ', 'ِ',
        'ّ', 'ْ', 'ٓ', 'ٔ', 'ٕ', 'ٖ',
        'ٗ', '٘', 'ٰ',
    };

    // Words that add no signal — articles, generic "street", filler.
    // Compared after lowercasing. Egyptian variants and Arabic equivalents
    // included.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "el", "al", "the",
        "st", "str", "street", "rd", "road", "ave", "avenue",
        "shara",                                         // شارع — Buckwalter sh+a+r+a (ayn→a)
        "shaara", "shari", "sharia", "shar", "shaar",   // common romanizations of شارع
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            if (Diacritics.Contains(ch)) continue;

            if (ArabicToLatin.TryGetValue(ch, out var latin))
            {
                sb.Append(latin);
                continue;
            }

            // Letters and digits pass through; everything else becomes a space
            // so we don't accidentally fuse tokens across punctuation.
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(' ');
            }
        }

        // Tokenize, drop stopwords, rejoin.
        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !StopWords.Contains(t))
            .ToArray();

        return string.Join(' ', tokens);
    }
}
