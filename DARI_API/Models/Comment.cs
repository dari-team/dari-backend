using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Comment
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid WishlistItemId { get; set; }

        [ForeignKey(nameof(WishlistItemId))]
        public WishlistItem WishlistItem { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(255)")]
        public string Text { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }
    }
}
