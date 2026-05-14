using System.ComponentModel.DataAnnotations;
using DARI_API.Models;

namespace DARI_API.ViewModels
{
    public class CreateListingRequest
    {
        [Required, MaxLength(255)]
        public string Title { get; set; } = "";

        [Required, MinLength(10)]
        public string Description { get; set; } = "";

        [Required, Range(0, 999999999)]
        public decimal Price { get; set; }

        [Range(0, 20)]
        public int Bedrooms { get; set; }

        [Range(0, 20)]
        public int Bathrooms { get; set; }

        [Required, Range(1, 100000)]
        public decimal AreaSize { get; set; }

        [Required]
        public PropertyType PropertyType { get; set; }

        [MaxLength(100)]
        public string? Finishing { get; set; }

        [Required]
        public ListingType ListingType { get; set; }

        // Optional — client should send Residential=0 or Commercial=1.
        // If omitted, backend infers from PropertyType.
        public ListingKind? ListingKind { get; set; }

        [Required]
        public CreateAddressRequest Address { get; set; } = new();

        // Images already uploaded to Cloudinary (direct from browser with signed params).
        // Server verifies each public_id exists before persisting.
        public List<CreateImageRequest> Images { get; set; } = new();

        // Lifestyle score computed on client. Server stores as-is, never recomputes.
        public decimal? LifestyleScore { get; set; }
        public string? LifestyleScoreBreakdown { get; set; } // JSON string

        // Amenity keys selected by the lister (e.g. ["elevator","balcony"]).
        // Optional — listers may submit a listing with none.
        public List<string> Amenities { get; set; } = new();
    }

    public class CreateAddressRequest
    {
        [Required, MaxLength(255)]
        public string Street { get; set; } = "";

        [Required, MaxLength(100)]
        public string City { get; set; } = "";

        [Required, MaxLength(100)]
        public string Region { get; set; } = "";

        [MaxLength(100)]
        public string Country { get; set; } = "Egypt";

        [Required, Range(-90, 90)]
        public decimal Latitude { get; set; }

        [Required, Range(-180, 180)]
        public decimal Longitude { get; set; }
    }

    public class CreateImageRequest
    {
        [Required, MaxLength(500)]
        public string Url { get; set; } = "";

        [Required, MaxLength(255)]
        public string PublicId { get; set; } = "";

        [MaxLength(20)]
        public string? Format { get; set; }

        public long? Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int SortOrder { get; set; }
    }
}
