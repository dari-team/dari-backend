using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DARI_API.Models
{
    public class WishlistCollaborator
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid WishlistId { get; set; }

        [ForeignKey(nameof(WishlistId))]
        public Wishlist Wishlist { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; }

        [Required]
        public DateTime AddedAt { get; set; }
    }
}