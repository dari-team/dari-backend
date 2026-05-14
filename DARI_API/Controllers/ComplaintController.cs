using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ComplaintController : Controller
    {
        private readonly ApplicationDbContext _db;

        public ComplaintController(ApplicationDbContext db)
        {
            _db = db;
        }

        // POST /api/Complaint — a logged-in user reports a listing.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateComplaintRequest req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == req.ListingId);
            if (listing == null)
                return NotFound(new { message = "Listing not found." });

            // One open report per user per listing — stops the same person
            // spamming reports while admins are still reviewing the first one.
            var hasOpen = await _db.Complaints.AnyAsync(c =>
                c.ListingId == req.ListingId &&
                c.ReporterId == userId &&
                c.Status == ComplaintStatus.Open);
            if (hasOpen)
                return Conflict(new { message = "You already have an open report on this listing." });

            var complaint = new Complaint
            {
                Id = Guid.NewGuid(),
                ListingId = req.ListingId,
                ReporterId = userId,
                Reason = req.Reason,
                Details = string.IsNullOrWhiteSpace(req.Details) ? null : req.Details.Trim(),
                Status = ComplaintStatus.Open,
                CreatedAt = DateTime.UtcNow
            };

            _db.Complaints.Add(complaint);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Report submitted. Our team will review it shortly.", id = complaint.Id });
        }
    }
}
