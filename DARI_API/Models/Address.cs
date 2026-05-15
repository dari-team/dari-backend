using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Address
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public Listing Listing { get; set; }

        [MaxLength(255)]
        [Column(TypeName ="nvarchar(255)")]
        public string Street { get; set; }

        [MaxLength(100)]
        [Column(TypeName = "nvarchar(255)")]
        public string City { get; set; }

        [MaxLength(100)]
        [Column(TypeName = "nvarchar(255)")]
        public string Region { get; set; }

        [MaxLength(100)]
        [Column(TypeName = "nvarchar(255)")]
        public string Country { get; set; }

        [Column(TypeName = "decimal(10,8)")]
        public decimal Latitude { get; set; }

        [Column(TypeName = "decimal(11,8)")]
        public decimal Longitude { get; set; }

        // Bilingual canonical forms produced by Gemini at INSERT time. The lister
        // types in either script; Gemini fills in the missing one. The UI picks
        // whichever matches the current language so an Arabic user never sees
        // "Abbas El Akkad" on one card and "عباس العقاد" on the next.
        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? StreetAr { get; set; }

        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? StreetLatin { get; set; }

        // Concatenation of both forms, lowercased + stop-word-stripped, fed to
        // the SQL Server full-text catalog so cross-script search hits both.
        // Stamped with NormalizationVersion so a future re-key job can find
        // stale rows after the algorithm changes.
        [MaxLength(255)]
        [Column(TypeName = "nvarchar(255)")]
        public string? StreetSearchKey { get; set; }

        [MaxLength(32)]
        [Column(TypeName = "nvarchar(32)")]
        public string? NormalizationVersion { get; set; }
    }
}
