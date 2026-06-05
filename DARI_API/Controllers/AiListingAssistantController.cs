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
            _httpClient = httpClientFactory.CreateClient("Groq");
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
        [HttpPost("standardize")]
        public async Task<IActionResult> Standardize(StandardizeListingViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.RawText))
                return BadRequest("Raw text is required.");

            var apiKey = _configuration["Groq:ApiKey"];
            var groqModel = _configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
            var baseUrl = _configuration["Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1";

            if (string.IsNullOrEmpty(apiKey))
                return StatusCode(500, "AI not configured.");

            var prompt = $@"You are a real estate data extraction and standardization system. The listing text below may be in Arabic or English. Extract and normalize all fields.

FINISHING NORMALIZATION RULES (map any variant to these exact values):
- fully_finished: super lux, super luxury, سوبر لوكس, فاخر, fully finished, full finish, تشطيب كامل, متشطب
- semi_finished: semi finished, نص تشطيب, نصف تشطيب
- core_shell: core & shell, core and shell, هيكل, على الخريطة, red brick
- furnished: furnished, مفروش, مفروشة, فرنيتشر
- unfurnished: unfurnished, غير مفروش, بدون فرنيشة

PROPERTY TYPE NORMALIZATION:
- apartment: شقة, flat, apartment
- villa: فيلا, villa, twinhouse, توين هاوس
- studio: استوديو, studio
- duplex: دوبلكس, duplex
- penthouse: بنتهاوس, penthouse
- office: مكتب, office, administrative
- shop: محل, shop, retail
- land: أرض, land, plot

Listing text:
{model.RawText}

Reply ONLY with this exact JSON (use null for any field not mentioned):
{{
  ""title"": ""concise suggested title"",
  ""propertyType"": ""apartment|villa|studio|duplex|penthouse|office|shop|land"",
  ""listingType"": ""sale|rent"",
  ""price"": 0,
  ""bedrooms"": 0,
  ""bathrooms"": 0,
  ""areaSize"": 0,
  ""finishing"": ""fully_finished|semi_finished|core_shell|furnished|unfurnished or null"",
  ""city"": ""city name in English"",
  ""description"": ""clean professional description without phone numbers or promotional spam"",
  ""tags"": [""tag1"", ""tag2"", ""tag3"", ""tag4"", ""tag5""]
}}";

            var requestBody = new
            {
                model = groqModel,
                messages = new[]
                {
                    new { role = "system", content = "You are a real estate data extraction system supporting Arabic and English. Always reply with JSON only. Normalize finishing and property type values to the exact enum strings provided." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 800,
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return StatusCode(500, "AI extraction failed.");

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (text == null) return StatusCode(500, "Empty AI response.");

            var result = System.Text.Json.JsonSerializer.Deserialize<StandardizeListingResponseViewModel>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return Ok(result);
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpPost("score-listing")]
        public async Task<IActionResult> ScoreListing(ScoreListingViewModel model)
        {
            var apiKey = _configuration["Groq:ApiKey"];
            var groqModel = _configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
            var baseUrl = _configuration["Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1";

            if (string.IsNullOrEmpty(apiKey))
                return StatusCode(500, "AI not configured.");

            var prompt = $@"You are a real estate listing quality evaluator. Score this listing on 4 dimensions (0-100 each).

Listing Data:
- Title: {model.Title}
- Description: {model.Description}
- Property Type: {model.PropertyType}
- Listing Type: {model.ListingType}
- Price: {model.Price} EGP
- Bedrooms: {model.Bedrooms}, Bathrooms: {model.Bathrooms}
- Area: {model.AreaSize} m²
- Finishing: {model.Finishing ?? "not specified"}
- City: {model.City ?? "not specified"}
- Photos uploaded: {model.PhotoCount}

SCORING CRITERIA:

1. completeness (0-100): Are all key fields provided? Title (10), description ≥50 chars (20), price > 0 (10), bedrooms & bathrooms (10), area (10), finishing (10), city (10), photos ≥3 (10), photos ≥6 bonus (+10). Deduct for missing fields.

2. descriptionQuality (0-100): Start at 100 — a detailed, professional description earns full marks. Deduct: under 100 chars -40 (or 100-200 chars -20); contains a phone number -30; ALL CAPS spam -20; doesn't mention key features (rooms, location, finishing, amenities) -20; not properly written Arabic/English -20. A clean, detailed (>200 chars), feature-rich description scores 100.

3. credibility (0-100): Does price seem reasonable for Egypt (not 0, not suspiciously low)? Is data consistent (e.g. studio shouldn't have 5 bedrooms)? No contradictions? Score 100 if all good, deduct for issues.

4. photoScore (0-100): 0 photos=0, 1-2=40, 3-5=70, 6-7=90, 8+=100.

Calculate overallScore = (completeness*0.3 + descriptionQuality*0.3 + credibility*0.2 + photoScore*0.2) rounded to integer.
A complete, credible listing with a strong description and 8+ photos should score close to 100 — do NOT artificially cap excellent listings below 100.

verdict: ""poor"" if overall<40, ""fair"" if 40-59, ""good"" if 60-79, ""excellent"" if >=80.

suggestions: list 2-4 specific actionable improvement tips based on weaknesses found. Keep them short (max 10 words each).

Reply ONLY with this exact JSON:
{{
  ""overallScore"": 0,
  ""completeness"": 0,
  ""descriptionQuality"": 0,
  ""credibility"": 0,
  ""photoScore"": 0,
  ""verdict"": ""poor|fair|good|excellent"",
  ""suggestions"": [""tip1"", ""tip2""]
}}";

            var requestBody = new
            {
                model = groqModel,
                messages = new[]
                {
                    new { role = "system", content = "You are a real estate listing quality auditor. Always reply with JSON only." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 600,
                temperature = 0.1,
                response_format = new { type = "json_object" }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return StatusCode(500, "AI scoring failed.");

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (text == null) return StatusCode(500, "Empty AI response.");

            var result = System.Text.Json.JsonSerializer.Deserialize<ScoreListingResponseViewModel>(text,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
            var apiKey = _configuration["Groq:ApiKey"];
            var model_ = _configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
            var baseUrl = _configuration["Groq:BaseUrl"] ?? "https://api.groq.com/openai/v1";

            if (string.IsNullOrEmpty(apiKey))
                return null;

            bool isEnglish = string.Equals(model.Language, "en", StringComparison.OrdinalIgnoreCase);

            var amenitiesEn = model.Amenities is { Count: > 0 } ? string.Join(", ", model.Amenities) : "none specified";
            var amenitiesAr = model.Amenities is { Count: > 0 } ? string.Join("، ", model.Amenities) : "غير محددة";

            var prompt = isEnglish ? $@"
You are a professional real estate copywriter. Write a compelling, unique property description in English.

Property details:
- Title: {model.Title}
- Property type: {model.PropertyType}
- Listing type: {model.ListingType}
- Price: {model.Price} EGP
- Bedrooms: {model.Bedrooms}
- Bathrooms: {model.Bathrooms}
- Area: {model.AreaSize} m²
- Finishing: {model.Finishing ?? "not specified"}
- Location: {model.Location ?? "not specified"}
- Payment method: {model.PaymentMethod ?? "not specified"}
- Completion status: {model.CompletionStatus ?? "not specified"}
- Amenities: {amenitiesEn}

Requirements:
1. Write a professional, engaging English description (3-4 sentences). Make it unique and vivid. Naturally weave in the most appealing amenities and the payment/completion details when relevant — do not just list them.
2. Suggest 5 relevant English keyword tags for this property, drawing on its location, type, and standout amenities.

Reply ONLY with JSON in this exact format:
{{
  ""description"": ""description here"",
  ""tags"": [""tag1"", ""tag2"", ""tag3"", ""tag4"", ""tag5""]
}}" : $@"
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
- طريقة الدفع: {model.PaymentMethod ?? "غير محددة"}
- حالة التسليم: {model.CompletionStatus ?? "غير محددة"}
- وسائل الراحة: {amenitiesAr}

المطلوب:
1. اكتب وصفاً احترافياً جذاباً ومميزاً للعقار باللغة العربية (3-4 جمل). اجعل كل وصف فريداً ومختلفاً، وادمج أبرز وسائل الراحة وتفاصيل الدفع والتسليم بشكل طبيعي عند الحاجة دون مجرد سردها.
2. اقترح قائمة من 5 تاجات (كلمات مفتاحية) مناسبة للعقار باللغة العربية، مستندة إلى الموقع والنوع وأبرز وسائل الراحة.

أجب فقط بصيغة JSON بالشكل التالي بدون أي نص إضافي:
{{
  ""description"": ""الوصف هنا"",
  ""tags"": [""تاج1"", ""تاج2"", ""تاج3"", ""تاج4"", ""تاج5""]
}}";

            var requestBody = new
            {
                model = model_,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = isEnglish
                            ? "You are a professional real estate copywriter. Always reply with JSON only."
                            : "أنت خبير عقاري محترف يكتب أوصافاً عقارية احترافية باللغة العربية. أجب دائماً بصيغة JSON فقط."
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

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
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