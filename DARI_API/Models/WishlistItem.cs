using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class WishlistItem
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid WishlistId { get; set; }

        [ForeignKey(nameof(WishlistId))]
        public Wishlist Wishlist { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public Listing Listing { get; set; }

        [Required]
        public Guid AddedBy { get; set; }

        [Column(TypeName = "decimal(3,2)")]
        public decimal? Rating { get; set; }

        [Required]
        public DateTime AddedAt { get; set; }

        public ICollection<Comment>? Comments { get; set; }
    }
}
