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

        public async Task<List<VisualSearchResultViewModel>> SearchAsync(IFormFile queryImage, int topN = 10)
        {
            using var content = new MultipartFormDataContent();
            using var stream = queryImage.OpenReadStream();
            content.Add(new StreamContent(stream), "file", queryImage.FileName);

            var response = await _httpClient.PostAsync($"{_cvServiceUrl}/encode", content);
            if (!response.IsSuccessStatusCode)
                throw new Exception("CV service failed to encode the image.");

            var result = await response.Content.ReadFromJsonAsync<EncodeResponse>();
            if (result?.Embedding == null)
                throw new Exception("CV service returned empty embedding.");

            float[] queryVector = result.Embedding;

            
            var allEmbeddings = await _unitOfWork.ImageEmbeddings.GetAllAsync();

            
            var scores = allEmbeddings
                .Select(e => new
                {
                    Embedding = e,
                    Score = CosineSimilarity(queryVector, JsonSerializer.Deserialize<float[]>(e.EmbeddingJson)!)
                })
                .OrderByDescending(x => x.Score)
                .Take(topN)
                .ToList();

            
            var results = new List<VisualSearchResultViewModel>();
            foreach (var item in scores)
            {
                var image = (await _unitOfWork.Images.FindAsync(i => i.Id == item.Embedding.ImageId)).FirstOrDefault();
                if (image == null) continue;

                var listing = (await _unitOfWork.Listings.FindAsync(l => l.Id == image.ListingId)).FirstOrDefault();
                if (listing == null) continue;

                results.Add(new VisualSearchResultViewModel
                {
                    ListingId = listing.Id,
                    Title = listing.Title,
                    ImageUrl = image.Url,
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
