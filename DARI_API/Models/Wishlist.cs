using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Wishlist
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid OwnerId { get; set; }

        [ForeignKey(nameof(OwnerId))]
        [InverseProperty("Wishlists")]
        public ApplicationUser Owner { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        public bool IsShared { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        public ICollection<WishlistItem>? Items { get; set; }

        public ICollection<WishlistCollaborator>? Collaborators { get; set; }
    }
}
