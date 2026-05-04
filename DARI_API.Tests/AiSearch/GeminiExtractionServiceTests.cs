using System.Net;
using System.Text;
using DARI_API.AiSearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DARI_API.Tests.AiSearch;

// Tests Layer 2's contract for the Gemini provider via a mocked
// HttpMessageHandler. Mirrors GroqExtractionServiceTests so both providers
// satisfy the same IAiExtractionService invariants.
public class GeminiExtractionServiceTests
{
    private static GeminiExtractionService BuildService(MockHandler handler)
    {
        var factory = new MockHttpFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"]  = "AIza_test_key",
                ["Gemini:Model"]   = "gemini-2.5-flash",
                ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/v1beta",
            }).Build();
        return new GeminiExtractionService(factory, config, NullLogger<GeminiExtractionService>.Instance);
    }

    [Fact]
    public async Task Successful_response_is_parsed()
    {
        var content = """
        {"property_type":"apartment","listing_type":"buy","location":"Maadi","location_text":null,
         "near_metro":null,"price_min":null,"price_max":1000000,"bedrooms":3,"bathrooms":null,
         "suggested_bedrooms":null,"area_min":null,"finishing_level":"FullyFinished",
         "payment_method":null,"max_down_payment":null}
        """;
        var svc = BuildService(MockHandler.OkWithContent(content));
        var result = await svc.ExtractAsync("3-bedroom apartment in Maadi under 1M");

        Assert.Equal("apartment", result.Parsed.PropertyType);
        Assert.Equal("Maadi", result.Parsed.Location);
        Assert.Equal(3, result.Parsed.Bedrooms);
        Assert.Equal(1_000_000m, result.Parsed.PriceMax);
        Assert.False(result.RetryUsed);
        Assert.Equal("gemini-2.5-flash", result.ModelUsed);
    }

    [Fact]
    public async Task Server_5xx_triggers_retry_then_succeeds()
    {
        var success = """{"property_type":"villa","listing_type":null,"location":null,"location_text":null,"near_metro":null,"price_min":null,"price_max":null,"bedrooms":null,"bathrooms":null,"suggested_bedrooms":null,"area_min":null,"finishing_level":null,"payment_method":null,"max_down_payment":null}""";
        var handler = MockHandler.Sequence(
            MockHandler.Failure(HttpStatusCode.InternalServerError, "boom"),
            MockHandler.OkWithContent(success));
        var svc = BuildService(handler);

        var result = await svc.ExtractAsync("villa");
        Assert.Equal("villa", result.Parsed.PropertyType);
        Assert.True(result.RetryUsed);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Two_consecutive_5xx_throws_AiExtractionException()
    {
        var handler = MockHandler.Sequence(
            MockHandler.Failure(HttpStatusCode.BadGateway,         "first"),
            MockHandler.Failure(HttpStatusCode.ServiceUnavailable, "second"));
        var svc = BuildService(handler);

        await Assert.ThrowsAsync<AiExtractionException>(
            () => svc.ExtractAsync("query"));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Client_4xx_throws_immediately_no_retry()
    {
        // 401/403/etc. are not retryable — a bad key won't fix itself.
        var handler = MockHandler.Failure(HttpStatusCode.Unauthorized, "bad key");
        var svc = BuildService(handler);

        await Assert.ThrowsAsync<AiExtractionException>(
            () => svc.ExtractAsync("query"));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Empty_query_throws_without_calling_api()
    {
        var handler = MockHandler.Failure(HttpStatusCode.InternalServerError, "should not be called");
        var svc = BuildService(handler);

        await Assert.ThrowsAsync<AiExtractionException>(() => svc.ExtractAsync(""));
        await Assert.ThrowsAsync<AiExtractionException>(() => svc.ExtractAsync("   "));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Language_detection_marks_arabic_input_as_ar()
    {
        var content = """{"property_type":null,"listing_type":null,"location":null,"location_text":null,"near_metro":null,"price_min":null,"price_max":null,"bedrooms":null,"bathrooms":null,"suggested_bedrooms":null,"area_min":null,"finishing_level":null,"payment_method":null,"max_down_payment":null}""";
        var svc = BuildService(MockHandler.OkWithContent(content));
        var result = await svc.ExtractAsync("شقة في المعادي");
        Assert.Equal("ar", result.Language);
    }

    [Fact]
    public async Task Token_usage_is_captured_from_metadata()
    {
        var content = """{"property_type":"studio","listing_type":null,"location":null,"location_text":null,"near_metro":null,"price_min":null,"price_max":null,"bedrooms":null,"bathrooms":null,"suggested_bedrooms":null,"area_min":null,"finishing_level":null,"payment_method":null,"max_down_payment":null}""";
        var svc = BuildService(MockHandler.OkWithContent(content));
        var result = await svc.ExtractAsync("studio");

        Assert.Equal(1500, result.PromptTokens);
        Assert.Equal(110,  result.CompletionTokens);
    }

    // --- Helpers ---

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

        public static MockHandler Sequence(params MockHandler[] handlers) =>
            new(handlers.SelectMany(h => h._responses));

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

        // Wrap inner ParsedQuery JSON inside Gemini's response envelope.
        private static string WrapInGeminiResponse(string innerJson)
        {
            var escaped = System.Text.Json.JsonEncodedText.Encode(innerJson).Value;
            return $$"""
            {
              "candidates": [
                { "content": { "parts": [ { "text": "{{escaped}}" } ] } }
              ],
              "usageMetadata": { "promptTokenCount": 1500, "candidatesTokenCount": 110 }
            }
            """;
        }
    }
}
