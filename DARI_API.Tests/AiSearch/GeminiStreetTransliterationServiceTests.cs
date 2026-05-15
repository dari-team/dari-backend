using System.Net;
using System.Text;
using DARI_API.AiSearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Tests the bilingual transliteration provider. Contract: never throw. Caller
// (ListingController) treats Ok=false as "store the typed form alone and flag
// the row for backfill" — so what's important is that the mapping from
// Gemini's responses to our StreetTransliteration result is correct.
public class GeminiStreetTransliterationServiceTests
{
    private static GeminiStreetTransliterationService BuildService(MockHandler handler)
    {
        var factory = new MockHttpFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"]  = "AIza_test_key",
                ["Gemini:Model"]   = "gemini-2.5-flash",
                ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta",
            }).Build();
        return new GeminiStreetTransliterationService(
            factory, config, NullLogger<GeminiStreetTransliterationService>.Instance);
    }

    [Fact]
    public async Task Latin_input_gets_arabic_counterpart()
    {
        var svc = BuildService(MockHandler.OkWithContent(
            """{"ar":"عباس العقاد","latin":"Abbas El Akkad"}"""));

        var r = await svc.TransliterateAsync("Abbas El Akkad");

        Assert.True(r.Ok);
        Assert.Equal("عباس العقاد", r.Ar);
        Assert.Equal("Abbas El Akkad", r.Latin);
    }

    [Fact]
    public async Task Arabic_input_gets_latin_counterpart()
    {
        var svc = BuildService(MockHandler.OkWithContent(
            """{"ar":"عباس العقاد","latin":"Abbas El Akkad"}"""));

        var r = await svc.TransliterateAsync("عباس العقاد");

        Assert.True(r.Ok);
        Assert.Equal("عباس العقاد", r.Ar);
        Assert.Equal("Abbas El Akkad", r.Latin);
    }

    [Fact]
    public async Task Empty_input_returns_not_ok_without_calling_gemini()
    {
        var handler = MockHandler.OkWithContent(
            """{"ar":"عباس العقاد","latin":"Abbas El Akkad"}""");
        var svc = BuildService(handler);

        var r = await svc.TransliterateAsync("");

        Assert.False(r.Ok);
        Assert.Null(r.Ar);
        Assert.Null(r.Latin);
        Assert.Equal(0, handler.CallCount);
    }

    // Listing creation must NEVER block on Gemini being unavailable.
    [Fact]
    public async Task Gemini_5xx_returns_degraded_result_does_not_throw()
    {
        var svc = BuildService(MockHandler.Failure(HttpStatusCode.InternalServerError, "boom"));

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Gemini_429_quota_returns_degraded_result_does_not_throw()
    {
        var svc = BuildService(MockHandler.Failure((HttpStatusCode)429, "rate limited"));

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Gemini_4xx_returns_degraded_result_does_not_throw()
    {
        var svc = BuildService(MockHandler.Failure(HttpStatusCode.Unauthorized, "bad key"));

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Unparseable_response_returns_degraded_result()
    {
        var svc = BuildService(MockHandler.OkWithContent("not even close to json"));

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Both_forms_empty_in_response_treated_as_failure()
    {
        var svc = BuildService(MockHandler.OkWithContent("""{"ar":"","latin":""}"""));

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Whitespace_only_form_is_normalized_to_null()
    {
        var svc = BuildService(MockHandler.OkWithContent(
            """{"ar":"   ","latin":"El Maadi"}"""));

        var r = await svc.TransliterateAsync("El Maadi");

        Assert.True(r.Ok);
        Assert.Null(r.Ar);
        Assert.Equal("El Maadi", r.Latin);
    }

    [Fact]
    public async Task Missing_api_key_returns_degraded_result_without_http_call()
    {
        var handler = MockHandler.OkWithContent("""{"ar":"x","latin":"y"}""");
        var factory = new MockHttpFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())  // no Gemini:ApiKey
            .Build();
        var svc = new GeminiStreetTransliterationService(
            factory, config, NullLogger<GeminiStreetTransliterationService>.Instance);

        var r = await svc.TransliterateAsync("Sheikh Zayed");

        Assert.False(r.Ok);
        Assert.Equal(0, handler.CallCount);
    }

    // ── Local mock infrastructure (mirrors GeminiExtractionServiceTests) ──

    private class MockHttpFactory : IHttpClientFactory
    {
        private readonly MockHandler _handler;
        public MockHttpFactory(MockHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private class MockHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public int CallCount { get; private set; }

        private MockHandler(IEnumerable<HttpResponseMessage> responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        public static MockHandler OkWithContent(string innerJson) =>
            new(new[] { Build(HttpStatusCode.OK, WrapInGeminiResponse(innerJson)) });

        public static MockHandler Failure(HttpStatusCode code, string body) =>
            new(new[] { Build(code, body) });

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("Mock has no more responses queued.");
            return Task.FromResult(_responses.Dequeue());
        }

        private static HttpResponseMessage Build(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static string WrapInGeminiResponse(string innerJson)
        {
            var escaped = System.Text.Json.JsonEncodedText.Encode(innerJson).Value;
            return $$"""
            {
              "candidates": [
                { "content": { "parts": [ { "text": "{{escaped}}" } ] } }
              ],
              "usageMetadata": { "promptTokenCount": 80, "candidatesTokenCount": 30 }
            }
            """;
        }
    }
}
