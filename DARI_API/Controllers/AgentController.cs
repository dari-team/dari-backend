using DARI_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    // Stats + analytics for listers ("agents"). All endpoints require auth.
    // Non-admin callers can only read their OWN stats / analytics.
    [ApiController]
    [Authorize(Roles = "Lister,Admin")]
    [Route("api/agent")]
    public class AgentController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AgentController(ApplicationDbContext db)
        {
            _db = db;
        }

        private Guid? CurrentUserId()
        {
            var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(s, out var id) ? id : (Guid?)null;
        }

        // GET /api/agent/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
            if (user == null) return NotFound("User not found.");

            var myListingIds = await _db.Listings
                .Where(l => l.ListerId == userId.Value)
                .Select(l => l.Id)
                .ToListAsync();

            var totalListings  = myListingIds.Count;
            var activeListings = await _db.Listings.CountAsync(l =>
                l.ListerId == userId.Value && l.IsApproved && l.Status == ListingStatus.Active);

            var totalInquiries   = await _db.Inquiries.CountAsync(i => i.ListerId == userId.Value);
            var pendingInquiries = await _db.Inquiries.CountAsync(i =>
                i.ListerId == userId.Value && i.Status == InquiryStatus.Pending);

            var totalViews = await _db.Listings
                .Where(l => l.ListerId == userId.Value)
                .SumAsync(l => (int?)l.ViewCount) ?? 0;

            // Saved count = wishlist items that reference my listings
            var savedCount = myListingIds.Count == 0 ? 0 :
                await _db.WishlistItems.CountAsync(w => myListingIds.Contains(w.ListingId));

            var now = DateTime.UtcNow;
            var subscriptionActive = user.SubscriptionEndDate.HasValue && user.SubscriptionEndDate.Value > now;

            return Ok(new
            {
                totalListings,
                activeListings,
                totalInquiries,
                pendingInquiries,
                totalViews,
                savedCount,
                subscriptionStatus = subscriptionActive ? "active" : "inactive",
                subscriptionExpiry = user.SubscriptionEndDate,
                maxListings = user.MaxListings,
                isVerified = user.IsVerified
            });
        }

        // GET /api/agent/listings/{id}/analytics
        [HttpGet("listings/{id}/analytics")]
        public async Task<IActionResult> GetListingAnalytics(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == id);
            if (listing == null) return NotFound();

            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && listing.ListerId != userId.Value) return Forbid();

            var now = DateTime.UtcNow;
            var since = now.Date.AddDays(-13); // 14-day window, inclusive of today

            var views = await _db.ListingViews
                .Where(v => v.ListingId == id)
                .ToListAsync();

            var totalViews = views.Count;

            // Views per day over full listing lifetime
            var daysLive = Math.Max(1, (now - listing.CreatedAt).TotalDays);
            var viewsPerDay = Math.Round(totalViews / daysLive, 2);

            // 14-day trend — zero-fill missing days so the chart has consistent axis
            var viewsTrend = Enumerable.Range(0, 14)
                .Select(offset =>
                {
                    var date = since.AddDays(offset);
                    var nextDate = date.AddDays(1);
                    var count = views.Count(v => v.ViewedAt >= date && v.ViewedAt < nextDate);
                    return new { date = date.ToString("yyyy-MM-dd"), count };
                })
                .ToList();

            // Traffic source breakdown (all-time)
            var trafficSources = new
            {
                search = views.Count(v => v.Source == ViewSource.Search),
                direct = views.Count(v => v.Source == ViewSource.Direct),
                saved  = views.Count(v => v.Source == ViewSource.Saved),
                map    = views.Count(v => v.Source == ViewSource.Map)
            };

            // Inquiries + status breakdown
            var inquiries = await _db.Inquiries
                .Where(i => i.ListingId == id)
                .ToListAsync();

            var inquiryStatusBreakdown = new
            {
                pending   = inquiries.Count(i => i.Status == InquiryStatus.Pending),
                responded = inquiries.Count(i => i.Status == InquiryStatus.Responded),
                closed    = inquiries.Count(i => i.Status == InquiryStatus.Closed)
            };

            var conversionRate = totalViews == 0 ? 0 : Math.Round(inquiries.Count * 100.0 / totalViews, 2);

            return Ok(new
            {
                listingId = id,
                title = listing.Title,
                totalViews,
                viewsPerDay,
                totalInquiries = inquiries.Count,
                conversionRate,
                aiQualityScore = listing.AiQualityScore,
                lifestyleScore = listing.LifestyleScore,
                viewsTrend14d = viewsTrend,
                trafficSources,
                inquiryStatusBreakdown
            });
        }
    }
}
