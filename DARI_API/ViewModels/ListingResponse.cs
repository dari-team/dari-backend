using System.Text.Json;
using DARI_API.Models;

namespace DARI_API.ViewModels
{
    public class ListingResponse
    {
        public Guid Id { get; set; }
        public Guid ListerId { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public decimal AreaSize { get; set; }
        public PropertyType PropertyType { get; set; }
        public string? Finishing { get; set; }
        public ListingType ListingType { get; set; }
        public ListingKind? ListingKind { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public CompletionStatus? CompletionStatus { get; set; }
        public int ReferenceNumber { get; set; }
        public ListingStatus Status { get; set; }
        public bool IsApproved { get; set; }
        public bool IsFeatured { get; set; }
        public int ViewCount { get; set; }
        public string? CoverImageUrl { get; set; }
        public decimal? LifestyleScore { get; set; }
        public string? LifestyleScoreBreakdown { get; set; }
        public DateTime? LifestyleScoreCalculatedAt { get; set; }
        public string? AiGeneratedDescription { get; set; }
        public string? AiStandardizedFinishing { get; set; }
        public string? AiGeneratedTags { get; set; }
        public decimal? AiQualityScore { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Amenity keys, parsed from the JSON array stored on Listing.Amenities.
        public List<string> Amenities { get; set; } = new();

        public AddressResponse? Address { get; set; }
        public List<ImageResponse> Images { get; set; } = new();

        // The real owner of the listing — surfaced so the public listing page can
        // show who actually listed the property instead of a placeholder.
        public ListerResponse? Lister { get; set; }

        public static ListingResponse From(Listing l) => new()
        {
            Id = l.Id,
            ListerId = l.ListerId,
            Title = l.Title,
            Description = l.Description,
            Price = l.Price,
            Bedrooms = l.Bedrooms,
            Bathrooms = l.Bathrooms,
            AreaSize = l.AreaSize,
            PropertyType = l.PropertyType,
            Finishing = l.Finishing,
            ListingType = l.ListingType,
            ListingKind = l.ListingKind,
            PaymentMethod = l.PaymentMethod,
            CompletionStatus = l.CompletionStatus,
            ReferenceNumber = l.ReferenceNumber,
            Status = l.Status,
            IsApproved = l.IsApproved,
            IsFeatured = l.IsFeatured,
            ViewCount = l.ViewCount,
            CoverImageUrl = l.CoverImageUrl,
            LifestyleScore = l.LifestyleScore,
            LifestyleScoreBreakdown = l.LifestyleScoreBreakdown,
            LifestyleScoreCalculatedAt = l.LifestyleScoreCalculatedAt,
            AiGeneratedDescription = l.AiGeneratedDescription,
            AiStandardizedFinishing = l.AiStandardizedFinishing,
            AiGeneratedTags = l.AiGeneratedTags,
            AiQualityScore = l.AiQualityScore,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
            Amenities = ParseAmenities(l.Amenities),
            Address = l.Address == null ? null : new AddressResponse
            {
                Street = l.Address.Street,
                StreetAr = l.Address.StreetAr,
                StreetLatin = l.Address.StreetLatin,
                City = l.Address.City,
                Region = l.Address.Region,
                Country = l.Address.Country,
                Latitude = l.Address.Latitude,
                Longitude = l.Address.Longitude
            },
            Images = l.Images?.OrderBy(i => i.SortOrder).Select(i => new ImageResponse
            {
                Id = i.Id,
                Url = i.Url,
                PublicId = i.PublicId,
                Width = i.Width,
                Height = i.Height,
                SortOrder = i.SortOrder
            }).ToList() ?? new List<ImageResponse>(),
            Lister = l.Lister == null ? null : new ListerResponse
            {
                Id = l.Lister.Id,
                Name = l.Lister.Name,
                AgencyName = l.Lister.AgencyName,
                PhoneNumber = l.Lister.PhoneNumber,
                ListerType = l.Lister.ListerType,
                IsVerified = l.Lister.IsVerified
            }
        };

        // Stored as a JSON array string. Malformed/empty payloads collapse to [].
        private static List<string> ParseAmenities(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }
    }

    public class AddressResponse
    {
        // Original user-typed form — used as a fallback when the localized
        // pair below is missing (e.g. legacy rows from before bilingual storage).
        public string Street { get; set; } = "";
        // Bilingual canonical forms. Frontend picks streetAr when the UI is in
        // Arabic, streetLatin otherwise, falling back to Street if both are null.
        public string? StreetAr { get; set; }
        public string? StreetLatin { get; set; }
        public string City { get; set; } = "";
        public string Region { get; set; } = "";
        public string Country { get; set; } = "";
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }

    public class ListerResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? AgencyName { get; set; }
        public string? PhoneNumber { get; set; }
        public ListerType? ListerType { get; set; }
        public bool IsVerified { get; set; }
    }

    public class ImageResponse
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = "";
        public string? PublicId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int SortOrder { get; set; }
    }
}
