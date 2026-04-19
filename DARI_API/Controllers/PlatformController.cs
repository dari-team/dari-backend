using DARI_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DARI_API.Controllers
{
    // Public aggregate stats for the landing page / about page.
    [ApiController]
    [AllowAnonymous]
    [Route("api/platform")]
    public class PlatformController : Controller
    {
        private readonly ApplicationDbContext _db;

        public PlatformController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET /api/platform/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var activeQuery = _db.Listings
                .Where(l => l.IsApproved && l.Status == ListingStatus.Active);

            var totalActiveListings = await activeQuery.CountAsync();

            // Avg prices by listing type (ForSale=0, ForRent=1)
            var avgPriceSale = await activeQuery
                .Where(l => l.ListingType == ListingType.ForSale)
                .AverageAsync(l => (decimal?)l.Price) ?? 0m;

            var avgPriceRent = await activeQuery
                .Where(l => l.ListingType == ListingType.ForRent)
                .AverageAsync(l => (decimal?)l.Price) ?? 0m;

            // Top 5 cities by listing count (active only)
            var topCities = await _db.Listings
                .Where(l => l.IsApproved && l.Status == ListingStatus.Active
                            && l.Address != null && l.Address.City != null)
                .GroupBy(l => l.Address!.City)
                .Select(g => new { city = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToListAsync();

            var totalRegisteredUsers = await _db.Users.CountAsync();

            return Ok(new
            {
                totalActiveListings,
                avgPriceSale = Math.Round(avgPriceSale, 2),
                avgPriceRent = Math.Round(avgPriceRent, 2),
                topCities,
                totalRegisteredUsers
            });
        }
    }
}
