using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Inquiry
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        [InverseProperty("CustomerInquiries")]
        public ApplicationUser Customer { get; set; }

        [Required]
        public Guid ListingId { get; set; }

        [ForeignKey(nameof(ListingId))]
        public Listing Listing { get; set; }

        [Required]
        public Guid ListerId { get; set; }

        [ForeignKey(nameof(ListerId))]
        [InverseProperty("ListerInquiries")]
        public ApplicationUser Lister { get; set; }

        [Required]
        public InquiryStatus Status { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        public ICollection<Message>? Messages { get; set; }
    }
}
