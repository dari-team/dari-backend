using DARI_API.IServicesLayer;
using System.Net.Mail;
using System.Net;
using DARI_API.ViewModels;
using DARI_API.Models;
using System.Text.Json;

namespace DARI_API.ServicesLayer
{
    public class ServiceLayer : IServiceLayer
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceLayer> _logger;
        private readonly IUnitOfWork _unitOfWork;
        private readonly HttpClient _httpClient;
        private readonly string _cvServiceUrl;

        public ServiceLayer(IConfiguration configuration, ILogger<ServiceLayer> logger, IUnitOfWork unitOfWork, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _httpClient = httpClientFactory.CreateClient("CVService");
            _cvServiceUrl = configuration["CVService:BaseUrl"] ?? "http://localhost:8000";
        }

        public async Task IndexImageAsync(Guid imageId, string imageUrl)
        {
            var existing = await _unitOfWork.ImageEmbeddings.FindAsync(e => e.ImageId == imageId);
            if (existing.Any()) return;

            
            var response = await _httpClient.PostAsJsonAsync(
                $"{_cvServiceUrl}/encode-url",
                new { url = imageUrl }
            );

            if (!response.IsSuccessStatusCode) return;

            var result = await response.Content.ReadFromJsonAsync<EncodeResponse>();
            if (result?.Embedding == null) return;

            var embedding = new ImageEmbedding
            {
                ImageId = imageId,
                EmbeddingJson = JsonSerializer.Serialize(result.Embedding)
            };

            await _unitOfWork.ImageEmbeddings.AddAsync(embedding);
            await _unitOfWork.SaveAsync();
        }

        public async Task<List<VisualSearchResultViewModel>> SearchAsync(VisualSearchRequestViewModel request, int topN = 10)
        {
            var queryImage = request.Image;

            // ─── 1. Encode the query image via CV service ────────────────────────
            using var content = new MultipartFormDataContent();
            using var stream = queryImage.OpenReadStream();
            content.Add(new StreamContent(stream), "file", queryImage.FileName);

            var response = await _httpClient.PostAsync($"{_cvServiceUrl}/encode", content);
            if (!response.IsSuccessStatusCode)
                throw new Exception("CV service failed to encode the image.");

            var encodeResult = await response.Content.ReadFromJsonAsync<EncodeResponse>();
            if (encodeResult?.Embedding == null)
                throw new Exception("CV service returned empty embedding.");

            float[] queryVector = encodeResult.Embedding;

            // ─── 2. Apply metadata pre-filter on listings (option #2) ────────────
            var listings = (await _unitOfWork.Listings.GetAllAsync()).ToList();

            if (!string.IsNullOrWhiteSpace(request.PropertyType) &&
                Enum.TryParse<Models.PropertyType>(request.PropertyType, true, out var pt))
                listings = listings.Where(l => l.PropertyType == pt).ToList();

            if (!string.IsNullOrWhiteSpace(request.ListingType))
            {
                // accept "buy"/"rent" as well as the enum names
                var lt = request.ListingType.Equals("buy", StringComparison.OrdinalIgnoreCase) ? Models.ListingType.ForSale
                       : request.ListingType.Equals("rent", StringComparison.OrdinalIgnoreCase) ? Models.ListingType.ForRent
                       : (Enum.TryParse<Models.ListingType>(request.ListingType, true, out var parsed) ? parsed : (Models.ListingType?)null);
                if (lt.HasValue) listings = listings.Where(l => l.ListingType == lt.Value).ToList();
            }

            if (!string.IsNullOrWhiteSpace(request.City))
            {
                var city = request.City.Trim();
                listings = listings.Where(l => l.Address != null &&
                    !string.IsNullOrEmpty(l.Address.City) &&
                    l.Address.City.Equals(city, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (request.BedsMin.HasValue) listings = listings.Where(l => l.Bedrooms >= request.BedsMin.Value).ToList();
            if (request.BedsMax.HasValue) listings = listings.Where(l => l.Bedrooms <= request.BedsMax.Value).ToList();

            if (listings.Count == 0) return new List<VisualSearchResultViewModel>();

            var listingIds = listings.Select(l => l.Id).ToHashSet();

            // ─── 3. Load embeddings + their images, restricted to filtered listings ─
            var allImages = (await _unitOfWork.Images.GetAllAsync())
                .Where(i => listingIds.Contains(i.ListingId))
                .ToList();
            var imageIdToListingId = allImages.ToDictionary(i => i.Id, i => i.ListingId);
            var imageIdToUrl       = allImages.ToDictionary(i => i.Id, i => i.Url);

            var allEmbeddings = (await _unitOfWork.ImageEmbeddings.GetAllAsync())
                .Where(e => imageIdToListingId.ContainsKey(e.ImageId))
                .ToList();

            if (allEmbeddings.Count == 0) return new List<VisualSearchResultViewModel>();

            // ─── 4. Pool embeddings per listing (option #1) ──────────────────────
            // For each listing, compute the L2-normalized mean of its image
            // embeddings. Also track the single best-matching image so we can
            // show a representative thumbnail in the UI.
            var perListing = new Dictionary<Guid, (float[] mean, Guid bestImageId, float bestImageScore)>();
            foreach (var grp in allEmbeddings.GroupBy(e => imageIdToListingId[e.ImageId]))
            {
                float[]? sum = null;
                int count = 0;
                Guid bestId = Guid.Empty;
                float bestScore = float.NegativeInfinity;

                foreach (var e in grp)
                {
                    var vec = JsonSerializer.Deserialize<float[]>(e.EmbeddingJson);
                    if (vec == null || vec.Length == 0) continue;
                    // Defensive: when the CV backbone changes (e.g. ViT-B/32 →
                    // ViT-L/14), old embeddings have a different dimension.
                    // Silently skip those rather than crashing; they should be
                    // re-indexed via /api/VisualSearch/reindex.
                    if (vec.Length != queryVector.Length) continue;

                    sum ??= new float[vec.Length];
                    for (int i = 0; i < vec.Length; i++) sum[i] += vec[i];
                    count++;

                    var imgScore = CosineSimilarity(queryVector, vec);
                    if (imgScore > bestScore) { bestScore = imgScore; bestId = e.ImageId; }
                }
                if (sum == null || count == 0) continue;

                // mean + L2 normalize
                float norm = 0;
                for (int i = 0; i < sum.Length; i++) { sum[i] /= count; norm += sum[i] * sum[i]; }
                norm = MathF.Sqrt(norm);
                if (norm > 1e-9) for (int i = 0; i < sum.Length; i++) sum[i] /= norm;

                perListing[grp.Key] = (sum, bestId, bestScore);
            }

            // ─── 5. Score listings (not images) by cosine against pooled vector ──
            var ranked = perListing
                .Select(kv => new
                {
                    ListingId = kv.Key,
                    Score = CosineSimilarity(queryVector, kv.Value.mean),
                    BestImageId = kv.Value.bestImageId,
                })
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .ToList();

            var results = new List<VisualSearchResultViewModel>();
            foreach (var item in ranked)
            {
                var listing = listings.FirstOrDefault(l => l.Id == item.ListingId);
                if (listing == null) continue;

                results.Add(new VisualSearchResultViewModel
                {
                    ListingId = listing.Id,
                    Title = listing.Title,
                    ImageUrl = imageIdToUrl.GetValueOrDefault(item.BestImageId, listing.CoverImageUrl ?? ""),
                    SimilarityScore = Math.Round(item.Score * 100, 1)
                });
            }

            return results;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            
            float dot = 0;
            for (int i = 0; i < a.Length; i++)
                dot += a[i] * b[i];
            return dot;
        }

        private class EncodeResponse
        {
            public float[] Embedding { get; set; }
            public int Dimensions { get; set; }
        }


        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var email = _configuration["EmailSettings:Email"];
            var password = _configuration["EmailSettings:Password"];

            // Dev fallback: if SMTP credentials aren't configured, log the email
            // to the console instead of crashing. This lets local development
            // proceed without a Gmail app password.
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning(
                    "EmailSettings not configured. Logging email instead of sending.\n" +
                    "  To:      {To}\n" +
                    "  Subject: {Subject}\n" +
                    "  Body:    {Body}",
                    to, subject, body);
                return;
            }

            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(email, password),
                EnableSsl = true
            };

            var mail = new MailMessage(email, to, subject, body);

            await client.SendMailAsync(mail);
        }
    }
}
