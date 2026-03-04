using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Message
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid InquiryId { get; set; }

        [ForeignKey(nameof(InquiryId))]
        public Inquiry Inquiry { get; set; }

        [Required]
        public Guid SenderId { get; set; }

        [ForeignKey(nameof(SenderId))]
        public ApplicationUser Sender { get; set; }

        [Required]
        public string Text { get; set; }

        [Required]
        public DateTime SentAt { get; set; }

        public DateTime? ReadAt { get; set; }
    }
}
