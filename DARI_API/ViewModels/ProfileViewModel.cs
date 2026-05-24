using System.ComponentModel.DataAnnotations;
using DARI_API.Models;

public class ProfileViewModel
{
    [MaxLength(255)]
    public string? Name { get; set; }

    [MaxLength(11)]
    public string? PhoneNumber { get; set; }

    public CustomerType? CustomerType { get; set; }

    public ListerType? ListerType { get; set; }

    
    public string? AgencyName { get; set; }


    public string? LicenseNumber { get; set; }

    [MaxLength(500)]
    public string? ProfilePictureUrl { get; set; }
}
