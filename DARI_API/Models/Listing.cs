using Microsoft.AspNetCore.Http.HttpResults;
using static System.Net.Mime.MediaTypeNames;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace DARI_API.Models
{
    public class Listing
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid ListerId { get; set; }

        [ForeignKey(nameof(ListerId))]
        [InverseProperty("Listings")]
        public ApplicationUser Lister { get; set; }

        [Required]
        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string Title { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(MAX)")]
        public string Description { get; set; }

        // Bilingual versions of the lister's free-text fields, filled once at
        // create time by Gemini (see GeminiListingTranslationService). The lister
        // types in one language; we store both so the listing page can render
        // whichever the viewer's UI calls for. All nullable — pre-existing rows
        // and any row where translation failed stay valid, and the frontend
        // falls back to the original Title/Description.
        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? TitleAr { get; set; }

        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? TitleEn { get; set; }

        [Column(TypeName = "nvarchar(MAX)")]
        public string? DescriptionAr { get; set; }

        [Column(TypeName = "nvarchar(MAX)")]
        public string? DescriptionEn { get; set; }

        [Column(TypeName = "nvarchar(MAX)")]
        public string? AiGeneratedDescription { get; set; }

        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? AiStandardizedFinishing { get; set; }

        [Column(TypeName = "nvarchar(MAX)")]
        public string? LifestyleScoreBreakdown { get; set; }

        public DateTime? LifestyleScoreCalculatedAt { get; set; }

        [MaxLength(500)]
        [Column(TypeName = "nvarchar(500)")]
        public string? CoverImageUrl { get; set; }

        [Required]
        [Column(TypeName = "decimal(15,2)")]
        public decimal Price { get; set; }

        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal AreaSize { get; set; }

        [Required]
        public PropertyType PropertyType { get; set; }

        [MaxLength(100)]
        [Column(TypeName = "nvarchar(255)")]
        public string? Finishing { get; set; }

        [Required]
        public ListingType ListingType { get; set; }

        // Residential (apartments, villas, …) vs Commercial (offices, shops, land).
        // Nullable so existing rows without the column stay valid after migration.
        public ListingKind? ListingKind { get; set; }

        [Required]
        public ListingStatus Status { get; set; }

        public int ViewCount { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? AiQualityScore { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LifestyleScore { get; set; }

        public bool IsApproved { get; set; }

        [Column(TypeName = "nvarchar(255)")]
        public string? RejectionReason { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime UpdatedAt { get; set; }

        public Address? Address { get; set; }

        public ICollection<Image>? Images { get; set; }
        public bool IsFeatured { get; set; }

        [Column(TypeName = "nvarchar(255)")]
        public string? AiGeneratedTags { get; set; }

        // Amenities the lister selected, stored as a JSON array of stable string
        // keys (e.g. ["elevator","balcony"]). Nullable so pre-existing rows stay
        // valid after the migration. Canonical key list lives in the frontend.
        [Column(TypeName = "nvarchar(MAX)")]
        public string? Amenities { get; set; }

        // How the buyer can pay. Required on new listings; existing rows are
        // backfilled with Cash by the migration.
        [Required]
        public PaymentMethod PaymentMethod { get; set; }

        // Ready-to-move vs off-plan. Nullable — optional on the listing form.
        public CompletionStatus? CompletionStatus { get; set; }

        // Human-friendly sequential listing reference (e.g. 100000042), shown to
        // users instead of the GUID. Generated from a SQL sequence on insert.
        public int ReferenceNumber { get; set; }

        public ICollection<ListingView>? Views { get; set; }
        public ICollection<Complaint>? Complaints { get; set; }
    }
}
