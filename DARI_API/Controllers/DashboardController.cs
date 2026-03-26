using DARI_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DARI_API.Controllers
{ 
  [ApiController]
[Route("api/[controller]")]
 public class DashboardController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
    {
        _unitOfWork = unitOfWork;
        _userManager = userManager;
    }
        [Authorize]

        [HttpGet("mystats")]
    public async Task<IActionResult> GetMyStats()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var guidUserId = Guid.Parse(userId);
        var myListings = (await _unitOfWork.Listings.FindAsync(x => x.ListerId == guidUserId)).ToList();
        var myInquiries = (await _unitOfWork.Inquiries.FindAsync(x => x.CustomerId == guidUserId || x.ListerId == guidUserId)).ToList();

        return Ok(new
        {
            TotalListings = myListings.Count,
            TotalInquiries = myInquiries.Count,
            TotalViews = myListings.Sum(x => x.ViewCount)
        });
    }

    [HttpGet("all-inquiries")]
    public async Task<IActionResult> GetAllInquiries()
    {
        var inquiries = await _unitOfWork.Inquiries.GetAllAsync();
        return Ok(inquiries);
    }
 
    [HttpGet("platform-stats")]
    public async Task<IActionResult> GetPlatformStats()
    {
        var totalUsers = await _userManager.Users.CountAsync();
        var allListings = await _unitOfWork.Listings.GetAllAsync();
        var allInquiries = await _unitOfWork.Inquiries.GetAllAsync();

        return Ok(new
        {
            TotalUsers = totalUsers,
            TotalListings = allListings.Count(),
            TotalInquiries = allInquiries.Count(),
            TotalViews = allListings.Sum(x => x.ViewCount)
        });
    }
}
}



