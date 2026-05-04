using DARI_API.AiSearch;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Tests document Layer 1's contract:
//
//   1. Identical normalization for repeated calls — deterministic.
//   2. Diacritics are dropped.
//   3. Stop-words ("el", "al", "shar", "street") are dropped.
//   4. Hyphens, commas, numbers are handled cleanly.
//   5. Arabic↔Arabic match works regardless of formatting.
//   6. Latin↔Latin match works regardless of case/punctuation.
//
// Cross-language matching (Arabic ↔ Latin) is NOT promised by v1 and is
// covered by an explicit "documents the limitation" test below.
public class StreetNormalizerTests
{
    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, StreetNormalizer.Normalize(null));
        Assert.Equal(string.Empty, StreetNormalizer.Normalize(""));
        Assert.Equal(string.Empty, StreetNormalizer.Normalize("   "));
    }

    [Fact]
    public void Same_input_produces_same_output()
    {
        var a = StreetNormalizer.Normalize("شارع عباس العقاد");
        var b = StreetNormalizer.Normalize("شارع عباس العقاد");
        Assert.Equal(a, b);
        Assert.NotEqual(string.Empty, a);
    }

    [Fact]
    public void Arabic_diacritics_are_stripped()
    {
        var withTashkeel    = StreetNormalizer.Normalize("شَارِع عَبَّاس العَقَّاد");
        var withoutTashkeel = StreetNormalizer.Normalize("شارع عباس العقاد");
        Assert.Equal(withoutTashkeel, withTashkeel);
    }

    [Fact]
    public void Stopwords_el_al_the_and_street_are_dropped()
    {
        var a = StreetNormalizer.Normalize("Abbas El Akkad");
        var b = StreetNormalizer.Normalize("Abbas Akkad");
        var c = StreetNormalizer.Normalize("Abbas Al Akkad Street");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Hyphens_punctuation_and_extra_whitespace_collapse()
    {
        var a = StreetNormalizer.Normalize("Abbas-El-Akkad");
        var b = StreetNormalizer.Normalize("Abbas, El   Akkad");
        var c = StreetNormalizer.Normalize("  Abbas  El   Akkad   ");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Latin_case_is_normalized()
    {
        var lower = StreetNormalizer.Normalize("abbas el akkad");
        var upper = StreetNormalizer.Normalize("ABBAS EL AKKAD");
        var mixed = StreetNormalizer.Normalize("AbBas eL aKkAd");
        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void Arabic_to_Arabic_match_survives_extra_filler()
    {
        // Same street, different filler/articles in Arabic.
        var a = StreetNormalizer.Normalize("عباس العقاد");
        var b = StreetNormalizer.Normalize("شارع عباس العقاد");
        var c = StreetNormalizer.Normalize("١٥ شارع عباس العقاد");
        Assert.Equal(a, b);
        // Arabic-Indic digits are not letters; they collapse to whitespace.
        // The street name itself still matches once digits are tokenized away.
        Assert.Contains(a, c);
    }

    [Fact]
    public void Numbers_and_stopwords_in_address_normalize_consistently()
    {
        var a = StreetNormalizer.Normalize("90th Street");
        var b = StreetNormalizer.Normalize("90th street");
        var c = StreetNormalizer.Normalize("90th St");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Contains("90", a);   // number is preserved
        Assert.DoesNotContain("street", a);
        Assert.DoesNotContain(" st ", " " + a + " ");
    }

    [Fact]
    public void Same_arabic_street_with_different_writing_styles_match()
    {
        // Egyptian writers vary on alif-with-hamza, taa-marbuta, etc.
        var a = StreetNormalizer.Normalize("أحمد عرابي");
        var b = StreetNormalizer.Normalize("احمد عرابي");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Tatweel_and_shadda_do_not_change_the_key()
    {
        var a = StreetNormalizer.Normalize("عبـاس");
        var b = StreetNormalizer.Normalize("عباس");
        var c = StreetNormalizer.Normalize("عبّاس");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Version_is_stamped()
    {
        Assert.Equal("buckwalter-v1", StreetNormalizer.Version);
    }

    // This test documents the v1 cross-language limitation explicitly so
    // future contributors don't assume it's a bug. v2 will close this gap.
    [Fact]
    public void Cross_language_matching_is_a_known_v1_limitation()
    {
        var arabic = StreetNormalizer.Normalize("عباس العقاد");
        var latin  = StreetNormalizer.Normalize("Abbas El Akkad");
        // We don't promise these match in v1 — we promise consistency
        // within each script. Both should be non-empty and stable.
        Assert.NotEqual(string.Empty, arabic);
        Assert.NotEqual(string.Empty, latin);
    }
}
