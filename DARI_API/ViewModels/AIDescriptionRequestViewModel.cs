namespace DARI_API.ViewModels
{
    public class AIDescriptionRequestViewModel
    {
        public string Title { get; set; }
        public decimal Price { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public decimal AreaSize { get; set; }
        public string PropertyType { get; set; }
        public string ListingType { get; set; }
        public string? Finishing { get; set; }
        public string? Location { get; set; }
        /// <summary>Human-readable amenity labels (already localized by the client).</summary>
        public List<string>? Amenities { get; set; }
        public string? PaymentMethod { get; set; }
        public string? CompletionStatus { get; set; }
        /// <summary>"ar" (default) or "en"</summary>
        public string? Language { get; set; }
    }

    public class AIDescriptionResponseViewModel
    {
        public string Description { get; set; }
        public List<string> Tags { get; set; }
    }

    public class StandardizeListingViewModel
    {
        public string RawText { get; set; }
    }

    public class StandardizeListingResponseViewModel
    {
        public string? Title { get; set; }
        public string? PropertyType { get; set; }
        public string? ListingType { get; set; }
        public decimal? Price { get; set; }
        public int? Bedrooms { get; set; }
        public int? Bathrooms { get; set; }
        public decimal? AreaSize { get; set; }
        public string? Finishing { get; set; }
        public string? City { get; set; }
        public string? Description { get; set; }
        public List<string>? Tags { get; set; }
    }

    public class ScoreListingViewModel
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string PropertyType { get; set; }
        public string ListingType { get; set; }
        public decimal Price { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public decimal AreaSize { get; set; }
        public string? Finishing { get; set; }
        public string? City { get; set; }
        public int PhotoCount { get; set; }
    }

    public class ScoreListingResponseViewModel
    {
        public int OverallScore { get; set; }
        public int Completeness { get; set; }
        public int DescriptionQuality { get; set; }
        public int Credibility { get; set; }
        public int PhotoScore { get; set; }
        public string Verdict { get; set; }
        public List<string> Suggestions { get; set; }
    }
}