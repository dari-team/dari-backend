using System.ComponentModel.DataAnnotations;
using DARI_API.Models;
namespace DARI_API.ViewModels
{
    public class RegisterViewModel
    {
        [Required]
        public string Name { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [MinLength(6)]
        public string Password { get; set; }

        // Optional depending on user type
        public UserType UserType { get; set; }
        
        public CustomerType? CustomerType { get; set; }

        public ListerType? ListerType { get; set; }

        public string? AgencyName { get; set; }

        public string? LicenseNumber { get; set; }
    }
}
