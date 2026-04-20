using DARI_API.Models;
using DARI_API.Seeder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(
            IUnitOfWork unitOfWork,
            UserManager<ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        [Authorize(Roles = "Admin,Lister")]
        [HttpGet("stats")]
        public async Task<IActionResult> GetAgentStats()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var guidUserId = Guid.Parse(userId);

            var listings = (await _unitOfWork.Listings
                .FindAsync(x => x.ListerId == guidUserId)).ToList();

            var inquiries = (await _unitOfWork.Inquiries
                .FindAsync(x => x.ListerId == guidUserId)).ToList();

            return Ok(new
            {
                TotalListings = listings.Count,
                TotalInquiries = inquiries.Count,
                PendingInquiries = inquiries.Count(x => x.Status == InquiryStatus.Pending),
                TotalViews = listings.Sum(x => x.ViewCount),
                SubscriptionStatus = "Active",
                ExpiryDate = DateTime.UtcNow.AddMonths(1),
                IsVerified = true
            });
        }

        [Authorize(Roles = "Admin,Lister")]
        [HttpGet("listings/{id}/analytics")]
        public async Task<IActionResult> GetListingAnalytics(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);

            if (listing == null)
                return NotFound();

            var inquiries = (await _unitOfWork.Inquiries
                .FindAsync(x => x.ListingId == id)).ToList();

            var totalViews = listing.ViewCount;
            var totalInquiries = inquiries.Count;

            return Ok(new
            {
                TotalViews = totalViews,
                ViewsPerDay = totalViews / 14,
                TotalInquiries = totalInquiries,
                ConversionRate = totalViews == 0
                    ? 0
                    : (double)totalInquiries / totalViews,
                AIQualityScore = 85,
                LifestyleScore = 90,
                ViewsLast14Days = Enumerable.Range(1, 14)
                    .Select(day => new
                    {
                        Day = DateTime.UtcNow.AddDays(-day).ToShortDateString(),
                        Views = totalViews / 14
                    }),
                TrafficSources = new
                {
                    Search = 40,
                    Direct = 30,
                    Saved = 20,
                    Map = 10
                },
                InquiryStatus = new
                {
                    Pending = inquiries.Count(x => x.Status == InquiryStatus.Pending),
                    Closed = inquiries.Count(x => x.Status == InquiryStatus.Closed)
                }
            });
        }

        [Authorize(Roles = Roles.Admin)]
        [HttpGet("platform/stats")]
        public async Task<IActionResult> GetPlatformStats()
        {
            var totalUsers = await _userManager.Users.CountAsync();

            var listings = (await _unitOfWork.Listings.GetAllAsync()).ToList();

            return Ok(new
            {
                TotalActiveListings = listings.Count,

                AveragePrice = new
                {
                    Sale = listings.Any(x => x.ListingType == ListingType.ForSale)
                        ? listings.Where(x => x.ListingType == ListingType.ForSale).Average(x => x.Price)
                        : 0,

                    Rent = listings.Any(x => x.ListingType == ListingType.ForRent)
                        ? listings.Where(x => x.ListingType == ListingType.ForRent).Average(x => x.Price)
                        : 0
                },

                MostPopularCities = new List<object>(),

                TotalUsers = totalUsers
            });
        }
    }
}