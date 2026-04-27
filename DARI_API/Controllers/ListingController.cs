using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using DARI_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("Api/[controller]")]
    public class ListingController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICloudinaryService _cloudinary;
        private readonly ApplicationDbContext _db;

        public ListingController(IUnitOfWork unitOfWork, ICloudinaryService cloudinary, ApplicationDbContext db)
        {
            _unitOfWork = unitOfWork;
            _cloudinary = cloudinary;
            _db = db;
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        // Base query for reads: eager-load Address + Images so ListingResponse can map them.
        private IQueryable<Listing> ListingsWithRelations() =>
            _db.Listings.Include(l => l.Address).Include(l => l.Images);

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var listings = await ListingsWithRelations()
                .Where(x => x.IsApproved)
                .ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateListingRequest req)
        {
            var listerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(listerIdStr, out var listerId))
                return Unauthorized("Invalid user token");

            if (req.Images == null || req.Images.Count < 3)
                return BadRequest("At least 3 images are required.");

            foreach (var img in req.Images)
            {
                if (string.IsNullOrWhiteSpace(img.PublicId) || string.IsNullOrWhiteSpace(img.Url))
                    return BadRequest("Image missing publicId or url.");

                var ok = await _cloudinary.VerifyUploadedAsync(img.PublicId);
                if (!ok)
                    return BadRequest($"Image {img.PublicId} was not found in Cloudinary.");
            }

            var now = DateTime.UtcNow;
            var listingId = Guid.NewGuid();

            var listing = new Listing
            {
                Id = listingId,
                Title = req.Title,
                Description = req.Description,
                Price = req.Price,
                ListerId = listerId,
                Bedrooms = req.Bedrooms,
                Bathrooms = req.Bathrooms,
                AreaSize = req.AreaSize,
                PropertyType = req.PropertyType,
                Finishing = req.Finishing,
                ListingType = req.ListingType,
                ListingKind = req.ListingKind ?? InferListingKind(req.PropertyType),
                Status = ListingStatus.Pending,
                ViewCount = 0,
                IsApproved = false,
                IsFeatured = false,
                CreatedAt = now,
                UpdatedAt = now,
                LifestyleScore = req.LifestyleScore,
                LifestyleScoreBreakdown = req.LifestyleScoreBreakdown,
                LifestyleScoreCalculatedAt = req.LifestyleScore.HasValue ? now : null,
                CoverImageUrl = req.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url
            };

            await _unitOfWork.Listings.AddAsync(listing);

            var address = new Address
            {
                Id = Guid.NewGuid(),
                ListingId = listingId,
                Street = req.Address.Street,
                City = req.Address.City,
                Region = req.Address.Region,
                Country = req.Address.Country,
                Latitude = req.Address.Latitude,
                Longitude = req.Address.Longitude
            };
            await _unitOfWork.Addresses.AddAsync(address);

            var images = new List<Image>();
            foreach (var img in req.Images)
            {
                var image = new Image
                {
                    Id = Guid.NewGuid(),
                    ListingId = listingId,
                    Url = img.Url,
                    PublicId = img.PublicId,
                    Format = img.Format,
                    Bytes = img.Bytes,
                    Width = img.Width,
                    Height = img.Height,
                    SortOrder = img.SortOrder,
                    UploadedAt = now
                };
                images.Add(image);
                await _unitOfWork.Images.AddAsync(image);
            }

            await _unitOfWork.SaveAsync();

            listing.Address = address;
            listing.Images = images;
            return CreatedAtAction(nameof(GetById), new { id = listingId }, ListingResponse.From(listing));
        }

        // GET /api/Listing/{id}?source=search|direct|saved|map
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id, [FromQuery] string? source = null)
        {
            var listing = await ListingsWithRelations().FirstOrDefaultAsync(x => x.Id == id);
            if (listing == null)
                return NotFound();

            listing.ViewCount++;

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? userId = Guid.TryParse(userIdStr, out var uid) ? uid : (Guid?)null;

            var parsedSource = ViewSource.Direct;
            if (!string.IsNullOrWhiteSpace(source) &&
                Enum.TryParse<ViewSource>(source, ignoreCase: true, out var s))
            {
                parsedSource = s;
            }

            _db.ListingViews.Add(new ListingView
            {
                Id = Guid.NewGuid(),
                ListingId = listing.Id,
                UserId = userId,
                Source = parsedSource,
                ViewedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();

            return Ok(ListingResponse.From(listing));
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] CreateListingRequest req)
        {
            var existing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (existing == null)
                return NotFound();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && (!Guid.TryParse(userIdStr, out var userId) || userId != existing.ListerId))
                return Forbid();

            bool majorEdit =
                existing.PropertyType != req.PropertyType ||
                existing.ListingType != req.ListingType ||
                existing.Bedrooms != req.Bedrooms ||
                existing.Bathrooms != req.Bathrooms ||
                existing.AreaSize != req.AreaSize;

            existing.Title = req.Title;
            existing.Price = req.Price;
            existing.Description = req.Description;
            existing.Bedrooms = req.Bedrooms;
            existing.Bathrooms = req.Bathrooms;
            existing.AreaSize = req.AreaSize;
            existing.PropertyType = req.PropertyType;
            existing.Finishing = req.Finishing;
            existing.ListingType = req.ListingType;
            existing.ListingKind = req.ListingKind ?? InferListingKind(req.PropertyType);
            existing.UpdatedAt = DateTime.UtcNow;

            if (majorEdit && !isAdmin)
            {
                existing.IsApproved = false;
                existing.Status = ListingStatus.Pending;
                existing.RejectionReason = null;
            }

            _unitOfWork.Listings.Update(existing);
            await _unitOfWork.SaveAsync();
            return Ok(ListingResponse.From(existing));
        }

        [Authorize(Roles = "Lister,Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null)
                return NotFound();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && (!Guid.TryParse(userIdStr, out var userId) || userId != listing.ListerId))
                return Forbid();

            _unitOfWork.Listings.Delete(listing);
            await _unitOfWork.SaveAsync();
            return Ok("Listing Deleted");
        }

        [HttpGet("featured")]
        public async Task<IActionResult> GetFeatured()
        {
            var listings = await ListingsWithRelations()
                .Where(x => x.IsFeatured && x.IsApproved)
                .ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMine()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();
            var listings = await ListingsWithRelations()
                .Where(x => x.ListerId == userId)
                .ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        [HttpGet("filter")]
        public async Task<IActionResult> Filter(
            decimal? minPrice,
            decimal? maxPrice,
            int? bedrooms,
            int? bathrooms,
            decimal? minArea,
            decimal? maxArea,
            PropertyType? propertyType,
            ListingType? listingType,
            ListingKind? listingKind,
            string? finishing,
            string? city,
            string? region)
        {
            var q = ListingsWithRelations().Where(x => x.IsApproved);

            if (minPrice != null) q = q.Where(x => x.Price >= minPrice);
            if (maxPrice != null) q = q.Where(x => x.Price <= maxPrice);
            if (bedrooms != null) q = q.Where(x => x.Bedrooms >= bedrooms);
            if (bathrooms != null) q = q.Where(x => x.Bathrooms >= bathrooms);
            if (minArea != null) q = q.Where(x => x.AreaSize >= minArea);
            if (maxArea != null) q = q.Where(x => x.AreaSize <= maxArea);
            if (propertyType != null) q = q.Where(x => x.PropertyType == propertyType);
            if (listingType != null) q = q.Where(x => x.ListingType == listingType);
            if (listingKind != null) q = q.Where(x => x.ListingKind == listingKind);
            if (!string.IsNullOrWhiteSpace(finishing)) q = q.Where(x => x.Finishing == finishing);
            if (!string.IsNullOrWhiteSpace(city)) q = q.Where(x => x.Address != null && x.Address.City == city);
            if (!string.IsNullOrWhiteSpace(region)) q = q.Where(x => x.Address != null && x.Address.Region == region);

            var listings = await q.ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        [HttpGet("recommended")]
        public async Task<IActionResult> GetRecommended()
        {
            var listings = await ListingsWithRelations()
                .Where(x => x.IsApproved && x.AiQualityScore.HasValue)
                .OrderByDescending(x => x.AiQualityScore)
                .Take(5)
                .ToListAsync();

            return Ok(listings.Select(ListingResponse.From));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Admin moderation
        // ──────────────────────────────────────────────────────────────────────────

        [Authorize(Roles = "Admin")]
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            var listings = await ListingsWithRelations()
                .Where(x => !x.IsApproved && x.Status != ListingStatus.Archived)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound();

            listing.IsApproved = true;
            listing.Status = ListingStatus.Active;
            listing.RejectionReason = null;
            listing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing approved",
                Body = $"Your listing \"{listing.Title}\" is now live.",
                Type = NotificationType.ListingApproved,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(ListingResponse.From(listing));
        }

        public class RejectRequest
        {
            public string? Reason { get; set; }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/reject")]
        public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest? body)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null) return NotFound();

            var reason = string.IsNullOrWhiteSpace(body?.Reason)
                ? "Did not meet listing guidelines."
                : body!.Reason!.Trim();

            listing.IsApproved = false;
            listing.Status = ListingStatus.Archived;
            listing.RejectionReason = reason.Length > 255 ? reason.Substring(0, 255) : reason;
            listing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Listings.Update(listing);

            await _unitOfWork.Notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = listing.ListerId,
                Title = "Listing rejected",
                Body = $"Your listing \"{listing.Title}\" was rejected: {reason}",
                Type = NotificationType.ListingRejected,
                Seen = false,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return Ok(ListingResponse.From(listing));
        }

        // Infers Residential vs Commercial from property type numeric value.
        // Office=5, Shop=6, Land=7 are Commercial; everything else is Residential.
        private static ListingKind InferListingKind(PropertyType pt) =>
            (int)pt >= 5 ? ListingKind.Commercial : ListingKind.Residential;
    }
}