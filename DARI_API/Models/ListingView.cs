using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DARI_API.Models
{
    public class ListingView
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey("ListingId")]
        public Listing Listing { get; set; }

        // Nullable — anonymous visitors still generate view rows (for traffic-source analytics)
        public Guid? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required]
        public ViewSource Source { get; set; }

        [Required]
        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }
}
