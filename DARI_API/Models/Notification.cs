using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DARI_API.Models
{
    public class Notification
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public ApplicationUser User { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; }

        [Required]
        public string Body { get; set; }

        [Required]
        public NotificationType Type { get; set; }

        public bool Seen { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }
    }
}
