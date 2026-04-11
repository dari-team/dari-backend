using DARI_API.Models;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DARI_API.Controllers
{
    [Authorize(Roles = "Customer")]
    [ApiController]
    [Route("api/[controller]")]
    public class WishlistController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;

        public WishlistController(IUnitOfWork unitOfWork, UserManager<ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        private Guid GetUserId()
        {
            return Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        //Get all wishlists for the current user, including items and collaborators
        [HttpGet]
        public async Task<IActionResult> GetMyWishlists()
        {
            var userId = GetUserId();

            var wishlists = await _unitOfWork.Wishlists.FindAsync(x => x.OwnerId == userId);

            var result = new List<object>();

            foreach (var wishlist in wishlists)
            {
                var items = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == wishlist.Id);
                var collaborators = await _unitOfWork.WishlistCollaborators.FindAsync(x => x.WishlistId == wishlist.Id);

                result.Add(new
                {
                    wishlist.Id,
                    wishlist.Name,
                    wishlist.IsShared,
                    wishlist.CreatedAt,
                    Items = items.Select(i => new
                    {
                        i.Id,
                        i.ListingId,
                        i.Rating,
                        i.AddedAt,
                        i.AddedBy
                    }),
                    Collaborators = collaborators.Select(c => new
                    {
                        c.Id,
                        c.UserId,
                        c.AddedAt
                    })
                });
            }

            return Ok(result);
        }

        //Add a listing to a wishlist, either existing or new. If new, create the wishlist first.
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] WishlistAddViewModel model)
        {
            var userId = GetUserId();

            if (model.WishlistId == null && string.IsNullOrEmpty(model.NewWishlistName))
                return BadRequest("Provide either an existing WishlistId or a NewWishlistName.");

            if (model.WishlistId != null && !string.IsNullOrEmpty(model.NewWishlistName))
                return BadRequest("Provide either WishlistId or NewWishlistName, not both.");

            Wishlist wishlist;

            if (!string.IsNullOrEmpty(model.NewWishlistName))
            {
                wishlist = new Wishlist
                {
                    Id = Guid.NewGuid(),
                    OwnerId = userId,
                    Name = model.NewWishlistName,
                    IsShared = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.Wishlists.AddAsync(wishlist);
            }
            else
            {
                wishlist = await _unitOfWork.Wishlists.GetByIdAsync(model.WishlistId.Value);

                if (wishlist == null)
                    return NotFound("Wishlist not found");

                if (wishlist.OwnerId != userId)
                    return Forbid();
            }

            // Check if the listing is already in the wishlist to prevent duplicates
            var exists = (await _unitOfWork.WishlistItems.FindAsync(x =>
                x.WishlistId == wishlist.Id && x.ListingId == model.ListingId)).Any();

            if (exists)
                return BadRequest("This listing is already in this wishlist");

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

            return Ok(new { wishlist, item });
        }

        //update wishlist name or shared status
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateWishlist(Guid id, [FromBody] UpdateWishlistViewModel model)
        {
            var userId = GetUserId();

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(id);

            if (wishlist == null)
                return NotFound();

            if (wishlist.OwnerId != userId)
                return Forbid();

            if (!string.IsNullOrEmpty(model.Name))
                wishlist.Name = model.Name;

            if (model.IsShared.HasValue)
                wishlist.IsShared = model.IsShared.Value;

            _unitOfWork.Wishlists.Update(wishlist);
            await _unitOfWork.SaveAsync();

            return Ok(wishlist);
        }

        //Delete items and collaborators and wishlist
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWishlist(Guid id)
        {
            var userId = GetUserId();

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(id);

            if (wishlist == null)
                return NotFound();

            if (wishlist.OwnerId != userId)
                return Forbid();

            //Delete items
            var items = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == id);
            foreach (var item in items)
                _unitOfWork.WishlistItems.Delete(item);

            //Delete collaborators
            var collaborators = await _unitOfWork.WishlistCollaborators.FindAsync(x => x.WishlistId == id);
            foreach (var collaborator in collaborators)
                _unitOfWork.WishlistCollaborators.Delete(collaborator);

            _unitOfWork.Wishlists.Delete(wishlist);
            await _unitOfWork.SaveAsync();

            return Ok("Wishlist deleted");
        }

        //Delete an item from a wishlist and its comments
        [HttpDelete("items/{itemId}")]
        public async Task<IActionResult> RemoveItem(Guid itemId)
        {
            var userId = GetUserId();

            var item = await _unitOfWork.WishlistItems.GetByIdAsync(itemId);

            if (item == null)
                return NotFound("Wishlist item not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(item.WishlistId);

            if (wishlist == null || wishlist.OwnerId != userId)
                return Forbid();

            // Delete comments 
            var comments = await _unitOfWork.Comments.FindAsync(x => x.WishlistItemId == itemId);
            foreach (var comment in comments)
                _unitOfWork.Comments.Delete(comment);

            _unitOfWork.WishlistItems.Delete(item);
            await _unitOfWork.SaveAsync();

            return Ok("Removed from wishlist");
        }

        //Update rating (Only owner)
        [HttpPatch("items/{itemId}/rating")]
        public async Task<IActionResult> UpdateItemRating(Guid itemId, [FromBody] UpdateWishlistItemViewModel model)
        {
            var userId = GetUserId();

            var item = await _unitOfWork.WishlistItems.GetByIdAsync(itemId);

            if (item == null)
                return NotFound("Wishlist item not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(item.WishlistId);

            if (wishlist == null || wishlist.OwnerId != userId)
                return Forbid();

            if (model.Rating.HasValue && (model.Rating < 1 || model.Rating > 5))
                return BadRequest("Rating must be between 1 and 5");

            item.Rating = model.Rating;

            _unitOfWork.WishlistItems.Update(item);
            await _unitOfWork.SaveAsync();

            return Ok(item);
        }

        //Add collaborator by email (Owner only)
        [HttpPost("collaborators")]
        public async Task<IActionResult> AddCollaborator([FromBody] AddCollaboratorViewModel model)
        {
            var userId = GetUserId();

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(model.WishlistId);

            if (wishlist == null)
                return NotFound("Wishlist not found");

            if (wishlist.OwnerId != userId)
                return Forbid();

            if (!wishlist.IsShared)
                return BadRequest("Enable IsShared on this wishlist first.");

            var targetUser = await _userManager.FindByEmailAsync(model.Email);

            if (targetUser == null)
                return NotFound("User with that email not found");

            if (targetUser.Id == userId)
                return BadRequest("You cannot add yourself as a collaborator");

            var existing = (await _unitOfWork.WishlistCollaborators.FindAsync(x =>
                x.WishlistId == model.WishlistId && x.UserId == targetUser.Id)).FirstOrDefault();

            if (existing != null)
                return BadRequest("User is already a collaborator");

            var collaborator = new WishlistCollaborator
            {
                Id = Guid.NewGuid(),
                WishlistId = model.WishlistId,
                UserId = targetUser.Id,
                AddedAt = DateTime.UtcNow
            };

            await _unitOfWork.WishlistCollaborators.AddAsync(collaborator);
            await _unitOfWork.SaveAsync();

            return Ok(new
            {
                collaborator.Id,
                collaborator.WishlistId,
                collaborator.UserId,
                collaborator.AddedAt,
                CollaboratorName = targetUser.Name,
                CollaboratorEmail = targetUser.Email
            });
        }

        //Remove collaborator (Owner only)
        [HttpDelete("collaborators/{id}")]
        public async Task<IActionResult> RemoveCollaborator(Guid id)
        {
            var userId = GetUserId();

            var collaborator = await _unitOfWork.WishlistCollaborators.GetByIdAsync(id);

            if (collaborator == null)
                return NotFound("Collaborator not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(collaborator.WishlistId);

            if (wishlist == null || wishlist.OwnerId != userId)
                return Forbid();

            _unitOfWork.WishlistCollaborators.Delete(collaborator);
            await _unitOfWork.SaveAsync();

            return Ok("Collaborator removed");
        }
    }
}