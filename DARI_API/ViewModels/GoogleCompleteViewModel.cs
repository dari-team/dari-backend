using System.ComponentModel.DataAnnotations;
using DARI_API.Models;

namespace DARI_API.ViewModels
{
    // Sent by the frontend after a Google sign-in attempt returned
    // { needsProfile: true } — the user has picked their account type and
    // any lister-specific fields and is now finalising registration.
    public class GoogleCompleteViewModel
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;

        [Required]
        public UserType UserType { get; set; }

        public CustomerType? CustomerType { get; set; }
        public ListerType? ListerType { get; set; }

        public string? AgencyName { get; set; }
        public string? LicenseNumber { get; set; }
        public string? PhoneNumber { get; set; }
    }
}
