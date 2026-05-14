using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DARI_API.Models
{
    // A user-filed report against a listing. Reviewed by admins in the admin panel.
    public class Complaint
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public Listing Listing { get; set; }

        [Required]
        public Guid ReporterId { get; set; }

        [ForeignKey(nameof(ReporterId))]
        public ApplicationUser Reporter { get; set; }

        [Required]
        public ComplaintReason Reason { get; set; }

        // Optional free-text the reporter adds for context.
        [MaxLength(1000)]
        [Column(TypeName = "nvarchar(1000)")]
        public string? Details { get; set; }

        [Required]
        public ComplaintStatus Status { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        public DateTime? ReviewedAt { get; set; }

        // Admin who resolved it.
        public Guid? ReviewedBy { get; set; }
    }
}
