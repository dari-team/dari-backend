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
    }
}
