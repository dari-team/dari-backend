using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace DARI_API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AiListingAssistantController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IUnitOfWork _unitOfWork;
        private readonly HttpClient _httpClient;

        public AiListingAssistantController(IConfiguration configuration, IUnitOfWork unitOfWork, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _unitOfWork = unitOfWork;
            _httpClient = httpClientFactory.CreateClient("OpenAI");
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpPost("generate-description")]
        public async Task<IActionResult> GenerateDescription(AIDescriptionRequestViewModel model)
        {
            var result = await CallGroq(model);

            if (result == null)
                return StatusCode(500, "AI generation failed");

            return Ok(result);
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpPost("generate-and-apply/{listingId}")]
        public async Task<IActionResult> GenerateAndApply(Guid listingId, AIDescriptionRequestViewModel model)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(listingId);

            if (listing == null)
                return NotFound("Listing not found");

            var result = await CallGroq(model);

            if (result == null)
                return StatusCode(500, "AI generation failed");

            listing.Description = result.Description;
            listing.AiGeneratedTags = string.Join(",", result.Tags);
            listing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(listing);
            await _unitOfWork.SaveAsync();

            return Ok(new
            {
                listing.Id,
                listing.Title,
                result.Description,
                result.Tags
            });
        }

        private async Task<AIDescriptionResponseViewModel?> CallGroq(AIDescriptionRequestViewModel model)
        {
            var apiKey = _configuration["OpenAI:ApiKey"];

            if (string.IsNullOrEmpty(apiKey))
                return null;

            var prompt = $@"
أنت خبير عقاري محترف. مهمتك كتابة وصف عقاري احترافي وجذاب باللغة العربية.

تفاصيل العقار:
- العنوان: {model.Title}
- نوع العقار: {model.PropertyType}
- نوع الإعلان: {model.ListingType}
- السعر: {model.Price} جنيه
- غرف النوم: {model.Bedrooms}
- الحمامات: {model.Bathrooms}
- المساحة: {model.AreaSize} متر مربع
- التشطيب: {model.Finishing ?? "غير محدد"}
- الموقع: {model.Location ?? "غير محدد"}

المطلوب:
1. اكتب وصفاً احترافياً جذاباً للعقار باللغة العربية (3-4 جمل).
2. اقترح قائمة من 5 تاجات (كلمات مفتاحية) مناسبة للعقار باللغة العربية.

أجب فقط بصيغة JSON بالشكل التالي بدون أي نص إضافي:
{{
  ""description"": ""الوصف هنا"",
  ""tags"": [""تاج1"", ""تاج2"", ""تاج3"", ""تاج4"", ""تاج5""]
}}";

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "أنت خبير عقاري محترف يكتب أوصافاً عقارية احترافية باللغة العربية. أجب دائماً بصيغة JSON فقط."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                max_tokens = 1000,
                temperature = 0.7,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseBody);

            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (text == null)
                return null;

            return JsonSerializer.Deserialize<AIDescriptionResponseViewModel>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }
}