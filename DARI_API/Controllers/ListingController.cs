using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("Api/[controller]")]
    public class ListingController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        public ListingController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var listings = await _unitOfWork.Listings.FindAsync(x => x.IsApproved == true);
            return Ok(listings);
        }

        [HttpPost]
        public async Task<IActionResult> Create(ListingViewModel listing)
        {
            var userId = GetUserId();
            var data = new Listing
            {
                Id = Guid.NewGuid(),
                Title = listing.title,
                Price = listing.price,
                Description = listing.description,
                ListerId = userId,
                Bedrooms = listing.bedrooms,
                Bathrooms = listing.bathrooms,
                AreaSize = listing.areaSize,
                PropertyType = listing.propertyType,
                Finishing = listing.finishing,
                ListingType = listing.listingType,
                Status = ListingStatus.Pending,
                ViewCount = 0,
                IsApproved = false,
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await _unitOfWork.Listings.AddAsync(data);
            await _unitOfWork.SaveAsync();

            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null)
                return NotFound();

            listing.ViewCount++;
            await _unitOfWork.SaveAsync();

            return Ok(listing);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, ListingViewModel listing)
        {
            var existing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (existing == null)
                return NotFound();

            existing.Title = listing.title;
            existing.Price = listing.price;
            existing.Description = listing.description;
            existing.Bedrooms = listing.bedrooms;
            existing.Bathrooms = listing.bathrooms;
            existing.AreaSize = listing.areaSize;
            existing.PropertyType = listing.propertyType;
            existing.Finishing = listing.finishing;
            existing.ListingType = listing.listingType;
            existing.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Listings.Update(existing);
            await _unitOfWork.SaveAsync();
            return Ok(existing);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var listing = await _unitOfWork.Listings.GetByIdAsync(id);
            if (listing == null)
                return NotFound();
            _unitOfWork.Listings.Delete(listing);
            await _unitOfWork.SaveAsync();
            return Ok("Listing Deleted");
        }

        [HttpGet("featured")]
        public async Task<IActionResult> GetFeatured()
        {
            var listings = await _unitOfWork.Listings.FindAsync(x => x.IsFeatured == true && x.IsApproved == true);
            return Ok(listings);
        }

        [HttpGet("my/{userId}")]
        public async Task<IActionResult> GetMyListings()
        {
            var userId = GetUserId();
            var listings = await _unitOfWork.Listings.FindAsync(x => x.ListerId == userId);
            return Ok(listings);
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
            string? finishing)
        {
            var listings = await _unitOfWork.Listings.FindAsync(x => x.IsApproved == true);

            if (minPrice != null)
                listings = listings.Where(x => x.Price >= minPrice).ToList();
            if (maxPrice != null)
                listings = listings.Where(x => x.Price <= maxPrice).ToList();
            if (bedrooms != null)
                listings = listings.Where(x => x.Bedrooms == bedrooms).ToList();
            if (bathrooms != null)
                listings = listings.Where(x => x.Bathrooms == bathrooms).ToList();
            if (minArea != null)
                listings = listings.Where(x => x.AreaSize >= minArea).ToList();
            if (maxArea != null)
                listings = listings.Where(x => x.AreaSize <= maxArea).ToList();
            if (propertyType != null)
                listings = listings.Where(x => x.PropertyType == propertyType).ToList();
            if (listingType != null)
                listings = listings.Where(x => x.ListingType == listingType).ToList();
            if (finishing != null)
                listings = listings.Where(x => x.Finishing == finishing).ToList();

            return Ok(listings);
        }

        [HttpGet("recommended")]
        public async Task<IActionResult> GetRecommended()
        {
            var listings = await _unitOfWork.Listings.GetAllAsync();

            var recommended = listings
                .Where(x => x.IsApproved && x.AiQualityScore.HasValue)
                .OrderByDescending(x => x.AiQualityScore)
                .Take(5);

            return Ok(recommended);
        }

        [HttpPost("upload-image/{listingId}")]
        public async Task<IActionResult> UploadImage(Guid listingId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var listing = await _unitOfWork.Listings.GetByIdAsync(listingId);
            if (listing == null)
                return NotFound("Listing not found");

            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images");
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            var filePath = Path.Combine(folderPath, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            int width = 0, height = 0;
            using (var imgStream = file.OpenReadStream())
            {
                using var img = System.Drawing.Image.FromStream(imgStream);
                width = img.Width;
                height = img.Height;
            }

            var image = new Image
            {
                Id = Guid.NewGuid(),
                Url = "/images/" + fileName,
                ListingId = listingId,
                Width = width ,
                Height = height,
                UploadedAt = DateTime.UtcNow
            };

            await _unitOfWork.Images.AddAsync(image);
            await _unitOfWork.SaveAsync();
            return Ok(image);
        }
    }
}