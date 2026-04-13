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
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

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
        public string? Finishing { get; set; }

        [Required]
        public ListingType ListingType { get; set; }

        [Required]
        public ListingStatus Status { get; set; }

        public int ViewCount { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? AiQualityScore { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LifestyleScore { get; set; }

        public bool IsApproved { get; set; }
        
        public string? RejectionReason { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime UpdatedAt { get; set; }

        public Address? Address { get; set; }

        public ICollection<Image>? Images { get; set; }
        public bool IsFeatured { get; set; }

        public string? AiGeneratedTags { get; set; }
    }
}
