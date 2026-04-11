using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [Authorize(Roles = "Customer")]
    [ApiController]
    [Route("api/[controller]")]
    public class FavouriteController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        public FavouriteController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        [HttpPost]
        public async Task<IActionResult> Add(FavouriteViewModel model)
        {
            var userId = GetUserId();

            // Gets the user's wishlist
            var wishlist = (await _unitOfWork.Wishlists.FindAsync(x => x.OwnerId == userId)).FirstOrDefault();

            // If no wishlist, create one
            if (wishlist == null)
            {
                wishlist = new Wishlist
                {
                    Id = Guid.NewGuid(),
                    OwnerId = userId,
                    Name = "My Favourites",
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Wishlists.AddAsync(wishlist);
            }

            // Already in favourites check
            var exists = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == wishlist.Id && x.ListingId == model.ListingId);

            if (exists.Any())
                return BadRequest("Already in favourites");

            //Creates it
            var item = new WishlistItem
            {
                Id = Guid.NewGuid(),
                WishlistId = wishlist.Id,
                ListingId = model.ListingId,
                AddedBy = userId,
                AddedAt = DateTime.UtcNow
            };

            await _unitOfWork.WishlistItems.AddAsync(item);
            await _unitOfWork.SaveAsync();

            return Ok(item);
        }


        [HttpGet]
        public async Task<IActionResult> GetMyFavourites()
        {
            var userId = GetUserId();

            // Gets the user's wishlist
            var wishlist = (await _unitOfWork.Wishlists.FindAsync(x => x.OwnerId == userId)).FirstOrDefault();

            // If no wishlist, return empty list
            if (wishlist == null)
                return Ok(new List<WishlistItem>());

            var items = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == wishlist.Id);

            return Ok(items);
        }


        [HttpDelete("{listingId}")]
        public async Task<IActionResult> Remove(Guid listingId)
        {
            var userId = GetUserId();

            var wishlist = (await _unitOfWork.Wishlists.FindAsync(x => x.OwnerId == userId)).FirstOrDefault();

            if (wishlist == null)
                return NotFound();

            var item = (await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == wishlist.Id && x.ListingId == listingId)).FirstOrDefault();

            if (item == null)
                return NotFound();

            _unitOfWork.WishlistItems.Delete(item);
            await _unitOfWork.SaveAsync();

            return Ok("Removed from favourites");
        }
    }
}
