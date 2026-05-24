using DARI_API.AiSearch;
using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using DARI_API.IServicesLayer;
using DARI_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly IServiceLayer _visualSearchService;
        private readonly IStreetTransliterationService _streetTransliteration;
        private readonly IListingTranslationService _listingTranslation;

        public ListingController(
            IUnitOfWork unitOfWork,
            ICloudinaryService cloudinary,
            ApplicationDbContext db,
            IServiceLayer visualSearchService,
            IStreetTransliterationService streetTransliteration,
            IListingTranslationService listingTranslation)
        {
            _unitOfWork = unitOfWork;
            _cloudinary = cloudinary;
            _db = db;
            _visualSearchService = visualSearchService;
            _streetTransliteration = streetTransliteration;
            _listingTranslation = listingTranslation;
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        // Base query for reads: eager-load Address + Images + Lister so ListingResponse can map them.
        private IQueryable<Listing> ListingsWithRelations() =>
            _db.Listings.Include(l => l.Address).Include(l => l.Images).Include(l => l.Lister);

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
                PaymentMethod = req.PaymentMethod,
                CompletionStatus = req.CompletionStatus,
                Status = ListingStatus.Pending,
                ViewCount = 0,
                IsApproved = false,
                IsFeatured = false,
                CreatedAt = now,
                UpdatedAt = now,
                LifestyleScore = req.LifestyleScore,
                LifestyleScoreBreakdown = req.LifestyleScoreBreakdown,
                LifestyleScoreCalculatedAt = req.LifestyleScore.HasValue ? now : null,
                CoverImageUrl = req.Images.OrderBy(i => i.SortOrder).FirstOrDefault()?.Url,
                Amenities = SerializeAmenities(req.Amenities)
            };

            // Bilingual title/description via Gemini. The lister writes in one
            // language; we store both so the listing page can render whichever
            // the viewer's UI calls for. On any Gemini failure we leave the
            // columns null — listing creation must never block on a flaky API,
            // and the frontend falls back to the original Title/Description.
            var translation = await _listingTranslation.TranslateAsync(req.Title, req.Description);
            if (translation.Ok)
            {
                listing.TitleAr = translation.TitleAr;
                listing.TitleEn = translation.TitleEn;
                listing.DescriptionAr = translation.DescriptionAr;
                listing.DescriptionEn = translation.DescriptionEn;
            }

            await _unitOfWork.Listings.AddAsync(listing);

            // Bilingual canonical street pair via Gemini. The lister types in
            // one script; we fill the other so the UI can render whichever the
            // viewer's language calls for. On any Gemini failure we degrade
            // gracefully — listing creation must never block on a flaky API.
            var translit = await _streetTransliteration.TransliterateAsync(req.Address.Street);
            var (streetAr, streetLatin, normVersion) = BuildBilingualStreet(req.Address.Street, translit);

            var address = new Address
            {
                Id = Guid.NewGuid(),
                ListingId = listingId,
                Street = req.Address.Street,
                City = req.Address.City,
                Region = req.Address.Region,
                Country = req.Address.Country,
                Latitude = req.Address.Latitude,
                Longitude = req.Address.Longitude,
                StreetAr = streetAr,
                StreetLatin = streetLatin,
                StreetSearchKey = StreetNormalizer.Normalize(
                    string.Join(' ', new[] { streetAr, streetLatin }.Where(s => !string.IsNullOrWhiteSpace(s)))),
                NormalizationVersion = normVersion,
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

        // GET /api/Listing/{id}
        // Pure read — view tracking lives in POST /api/Listing/{id}/views so that
        // crawlers, link-preview bots, and prefetch don't inflate the count.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var listing = await ListingsWithRelations().FirstOrDefaultAsync(x => x.Id == id);
            if (listing == null)
                return NotFound();

            return Ok(ListingResponse.From(listing));
        }

        // POST /api/Listing/{id}/views?source=search|direct|saved|map
        // Records a view, deduped to one per visitor per listing per 24h. The
        // listing owner's own visits are never counted. Anonymous visitors are
        // identified by a salted hash of their IP + User-Agent (no raw IP stored).
        [HttpPost("{id}/views")]
        [EnableRateLimiting("listing-views")]
        public async Task<IActionResult> RecordView(Guid id, [FromQuery] string? source = null)
        {
            var listing = await _db.Listings.FirstOrDefaultAsync(x => x.Id == id);
            if (listing == null)
                return NotFound();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? userId = Guid.TryParse(userIdStr, out var uid) ? uid : (Guid?)null;

            // Owner viewing their own listing never counts.
            if (userId.HasValue && listing.ListerId == userId.Value)
                return NoContent();

            string? visitorHash = userId.HasValue ? null : ComputeVisitorHash();

            var since = DateTime.UtcNow.AddHours(-24);
            bool seenRecently = userId.HasValue
                ? await _db.ListingViews.AnyAsync(v =>
                    v.ListingId == id && v.UserId == userId.Value && v.ViewedAt >= since)
                : visitorHash != null && await _db.ListingViews.AnyAsync(v =>
                    v.ListingId == id && v.VisitorHash == visitorHash && v.ViewedAt >= since);

            if (seenRecently)
                return NoContent();

            var parsedSource = ViewSource.Direct;
            if (!string.IsNullOrWhiteSpace(source) &&
                Enum.TryParse<ViewSource>(source, ignoreCase: true, out var s))
            {
                parsedSource = s;
            }

            listing.ViewCount++;
            _db.ListingViews.Add(new ListingView
            {
                Id = Guid.NewGuid(),
                ListingId = listing.Id,
                UserId = userId,
                VisitorHash = visitorHash,
                Source = parsedSource,
                ViewedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveAsync();
            return NoContent();
        }

        // Salted SHA-256 of client IP + User-Agent. Never stores the raw IP.
        private string ComputeVisitorHash()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ua = Request.Headers.UserAgent.ToString();
            var salt = Environment.GetEnvironmentVariable("VIEW_HASH_SALT") ?? "dari-view-salt";
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{salt}|{ip}|{ua}"));
            return Convert.ToHexString(bytes).ToLowerInvariant();
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
            existing.PaymentMethod = req.PaymentMethod;
            existing.CompletionStatus = req.CompletionStatus;
            existing.Amenities = SerializeAmenities(req.Amenities);
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
            var listing = await _db.Listings
                .Include(l => l.Images)
                .FirstOrDefaultAsync(l => l.Id == id);
            if (listing == null)
                return NotFound();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = User.IsInRole("Admin");
            if (!isAdmin && (!Guid.TryParse(userIdStr, out var userId) || userId != listing.ListerId))
                return Forbid();

            // Delete the Cloudinary assets so storage doesn't fill up with dead
            // images. Best-effort — a Cloudinary hiccup must not block the delete.
            foreach (var img in listing.Images ?? new List<Image>())
            {
                if (string.IsNullOrWhiteSpace(img.PublicId)) continue;
                try { await _cloudinary.DeleteAsync(img.PublicId); }
                catch { /* best-effort */ }
            }

            // Image -> Listing FK is Restrict, so image rows must be removed first.
            _db.Images.RemoveRange(listing.Images ?? Enumerable.Empty<Image>());

            var address = await _db.Addresses.FirstOrDefaultAsync(a => a.ListingId == id);
            if (address != null) _db.Addresses.Remove(address);

            _db.ListingViews.RemoveRange(_db.ListingViews.Where(v => v.ListingId == id));

            // Inquiry -> Listing FK is Restrict — clear inquiries + their messages.
            var inquiries = await _db.Inquiries.Where(i => i.ListingId == id).ToListAsync();
            foreach (var inq in inquiries)
            {
                _db.Messages.RemoveRange(_db.Messages.Where(m => m.InquiryId == inq.Id));
                _db.Inquiries.Remove(inq);
            }

            // Complaints cascade-delete with the listing (FK configured Cascade).
            _db.Listings.Remove(listing);
            await _db.SaveChangesAsync();
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
            string? region,
            string? amenities,
            PaymentMethod? paymentMethod,
            CompletionStatus? completionStatus)
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
            if (completionStatus != null) q = q.Where(x => x.CompletionStatus == completionStatus);

            // paymentMethod: a listing marked "Both" satisfies a Cash or Installments filter.
            if (paymentMethod == PaymentMethod.Cash)
                q = q.Where(x => x.PaymentMethod == PaymentMethod.Cash || x.PaymentMethod == PaymentMethod.Both);
            else if (paymentMethod == PaymentMethod.Installments)
                q = q.Where(x => x.PaymentMethod == PaymentMethod.Installments || x.PaymentMethod == PaymentMethod.Both);

            // amenities = comma-separated keys; a listing must have ALL of them.
            // Amenities is a JSON array string, so we match the quoted key ("elevator")
            // to avoid one key being a substring of another.
            if (!string.IsNullOrWhiteSpace(amenities))
            {
                foreach (var key in amenities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var needle = $"\"{key}\"";
                    q = q.Where(x => x.Amenities != null && x.Amenities.Contains(needle));
                }
            }

            var listings = await q.ToListAsync();
            return Ok(listings.Select(ListingResponse.From));
        }

        // Serialize amenity keys to a JSON array string for storage on Listing.
        // Null/empty collapses to null so the column stays clean.
        private static string? SerializeAmenities(List<string>? keys)
        {
            if (keys == null || keys.Count == 0) return null;
            return JsonSerializer.Serialize(keys);
        }

        // Decides what to store for the Arabic + Latin canonical street pair
        // based on the user's typed input and Gemini's response. Three cases:
        //  1. Gemini succeeded → store both forms it returned, stamp ai-v1.
        //  2. Gemini failed but the typed form is detectably Arabic → store
        //     it as StreetAr, leave StreetLatin null, stamp fallback-typed.
        //  3. Gemini failed and typed form is non-Arabic → store as StreetLatin.
        // The null side will be filled by a future re-key admin job; for now
        // the user-facing UI falls back to the typed Street column when the
        // language-specific form is missing.
        private static (string? Ar, string? Latin, string Version) BuildBilingualStreet(
            string typed, StreetTransliteration result)
        {
            if (result.Ok)
                return (result.Ar, result.Latin, "ai-v1");

            var hasArabic = typed.Any(c => c >= 0x0600 && c <= 0x06FF);
            return hasArabic
                ? ((string?)typed, (string?)null, "fallback-typed")
                : ((string?)null, (string?)typed, "fallback-typed");
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