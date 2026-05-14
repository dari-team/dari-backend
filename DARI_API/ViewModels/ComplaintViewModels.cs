using System.ComponentModel.DataAnnotations;
using DARI_API.Models;

namespace DARI_API.ViewModels
{
    // A logged-in user filing a report against a listing.
    public class CreateComplaintRequest
    {
        [Required]
        public Guid ListingId { get; set; }

        [Required]
        public ComplaintReason Reason { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }
    }

    // Admin updates a complaint's status after reviewing it.
    public class ResolveComplaintRequest
    {
        [Required]
        public ComplaintStatus Status { get; set; }
    }
}
