using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Image
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public Listing Listing { get; set; }

        [Required]
        [MaxLength(500)]
        public string Url { get; set; }

        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? PublicId { get; set; }

        [MaxLength(20)]
        [Column(TypeName = "nvarchar(20)")]
        public string? Format { get; set; }

        public long? Bytes { get; set; }

        public int SortOrder { get; set; }

        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? Caption { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }

        [MaxLength(128)]
        public string? QdrantPointId { get; set; }

        [Required]
        public DateTime UploadedAt { get; set; }
    }
}
