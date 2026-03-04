using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace DARI_API.Models
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; }

        [Required]
        public AccountStatus AccountStatus { get; set; }

        [Required]
        public UserType UserType { get; set; }

        public CustomerType? CustomerType { get; set; }
        public ListerType? ListerType { get; set; }

        [MaxLength(100)]
        public string? LicenseNumber { get; set; }

        [MaxLength(255)]
        public string? AgencyName { get; set; }

        public bool IsVerified { get; set; }

        public int MaxListings { get; set; }

        public DateTime? SubscriptionEndDate { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public DateTime UpdatedAt { get; set; }

        // Navigation

        [InverseProperty("Lister")]
        public ICollection<Listing>? Listings { get; set; }

        [InverseProperty("Owner")]
        public ICollection<Wishlist>? Wishlists { get; set; }

        [InverseProperty("Customer")]
        public ICollection<Inquiry>? CustomerInquiries { get; set; }

        [InverseProperty("Lister")]
        public ICollection<Inquiry>? ListerInquiries { get; set; }

        public ICollection<Notification>? Notifications { get; set; }
    }
}
