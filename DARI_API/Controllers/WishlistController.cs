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

        private Guid GetUserId() =>
            Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // ── Enrich one wishlist with listing details + collaborator names ─────────
        private async Task<object> EnrichWishlist(Wishlist wishlist)
        {
            var items         = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == wishlist.Id);
            var collaborators = await _unitOfWork.WishlistCollaborators.FindAsync(x => x.WishlistId == wishlist.Id);

            // Enrich items with listing title / price / city / type
            var enrichedItems = new List<object>();
            foreach (var item in items.OrderBy(i => i.AddedAt))
            {
                var listing = await _unitOfWork.Listings.GetByIdAsync(item.ListingId);
                var address = listing != null
                    ? (await _unitOfWork.Addresses.FindAsync(a => a.ListingId == listing.Id)).FirstOrDefault()
                    : null;

                enrichedItems.Add(new
                {
                    item.Id,
                    item.ListingId,
                    item.Rating,
                    item.AddedAt,
                    item.AddedBy,
                    ListingTitle = listing?.Title ?? "",
                    ListingPrice = listing?.Price ?? 0,
                    ListingCity  = address?.City  ?? "",
                    ListingType  = listing?.ListingType,
                });
            }

            // Enrich collaborators with name + email
            var enrichedCollabs = new List<object>();
            foreach (var c in collaborators.OrderBy(c => c.AddedAt))
            {
                var user = await _userManager.FindByIdAsync(c.UserId.ToString());
                enrichedCollabs.Add(new
                {
                    c.Id,
                    c.UserId,
                    c.AddedAt,
                    Name  = user?.Name  ?? "",
                    Email = user?.Email ?? "",
                });
            }

            return new
            {
                wishlist.Id,
                wishlist.Name,
                wishlist.IsShared,
                wishlist.CreatedAt,
                Items         = enrichedItems,
                Collaborators = enrichedCollabs,
            };
        }

        // GET /api/Wishlist — all wishlists for current user (enriched)
        [HttpGet]
        public async Task<IActionResult> GetMyWishlists()
        {
            var userId    = GetUserId();
            var wishlists = await _unitOfWork.Wishlists.FindAsync(x => x.OwnerId == userId);

            var result = new List<object>();
            foreach (var wl in wishlists.OrderByDescending(x => x.CreatedAt))
                result.Add(await EnrichWishlist(wl));

            return Ok(result);
        }

        // POST /api/Wishlist — create wishlist (optionally add a listing at the same time)
        // If ListingId is null → create empty wishlist only.
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
                    Id        = Guid.NewGuid(),
                    OwnerId   = userId,
                    Name      = model.NewWishlistName,
                    IsShared  = model.IsShared ?? false,
                    CreatedAt = DateTime.UtcNow,
                };
                await _unitOfWork.Wishlists.AddAsync(wishlist);
            }
            else
            {
                wishlist = await _unitOfWork.Wishlists.GetByIdAsync(model.WishlistId!.Value);
                if (wishlist == null)           return NotFound("Wishlist not found");
                if (wishlist.OwnerId != userId) return Forbid();
            }

            // If no listing provided → just create the wishlist
            if (model.ListingId == null)
            {
                await _unitOfWork.SaveAsync();
                return Ok(await EnrichWishlist(wishlist));
            }

            // Check listing exists
            var listing = await _unitOfWork.Listings.GetByIdAsync(model.ListingId.Value);
            if (listing == null) return NotFound("Listing not found");

            // Duplicate guard
            var exists = (await _unitOfWork.WishlistItems.FindAsync(x =>
                x.WishlistId == wishlist.Id && x.ListingId == model.ListingId.Value)).Any();
            if (exists) return BadRequest("This listing is already in this wishlist");

            var item = new WishlistItem
            {
                Id          = Guid.NewGuid(),
                WishlistId  = wishlist.Id,
                ListingId   = model.ListingId.Value,
                AddedBy     = userId,
                AddedAt     = DateTime.UtcNow,
            };
            await _unitOfWork.WishlistItems.AddAsync(item);
            await _unitOfWork.SaveAsync();

            return Ok(await EnrichWishlist(wishlist));
        }

        // PATCH /api/Wishlist/{id} — rename or toggle IsShared
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateWishlist(Guid id, [FromBody] UpdateWishlistViewModel model)
        {
            var userId   = GetUserId();
            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(id);

            if (wishlist == null)           return NotFound();
            if (wishlist.OwnerId != userId) return Forbid();

            if (!string.IsNullOrEmpty(model.Name))
                wishlist.Name = model.Name;

            if (model.IsShared.HasValue)
                wishlist.IsShared = model.IsShared.Value;

            _unitOfWork.Wishlists.Update(wishlist);
            await _unitOfWork.SaveAsync();

            return Ok(await EnrichWishlist(wishlist));
        }

        // DELETE /api/Wishlist/{id} — delete wishlist + items + collaborators
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWishlist(Guid id)
        {
            var userId   = GetUserId();
            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(id);

            if (wishlist == null)           return NotFound();
            if (wishlist.OwnerId != userId) return Forbid();

            var items = await _unitOfWork.WishlistItems.FindAsync(x => x.WishlistId == id);
            foreach (var item in items)
                _unitOfWork.WishlistItems.Delete(item);

            var collaborators = await _unitOfWork.WishlistCollaborators.FindAsync(x => x.WishlistId == id);
            foreach (var c in collaborators)
                _unitOfWork.WishlistCollaborators.Delete(c);

            _unitOfWork.Wishlists.Delete(wishlist);
            await _unitOfWork.SaveAsync();

            return Ok("Wishlist deleted");
        }

        // DELETE /api/Wishlist/items/{itemId} — remove one item (and its comments)
        [HttpDelete("items/{itemId}")]
        public async Task<IActionResult> RemoveItem(Guid itemId)
        {
            var userId = GetUserId();
            var item   = await _unitOfWork.WishlistItems.GetByIdAsync(itemId);

            if (item == null) return NotFound("Wishlist item not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(item.WishlistId);
            if (wishlist == null || wishlist.OwnerId != userId) return Forbid();

            var comments = await _unitOfWork.Comments.FindAsync(x => x.WishlistItemId == itemId);
            foreach (var c in comments)
                _unitOfWork.Comments.Delete(c);

            _unitOfWork.WishlistItems.Delete(item);
            await _unitOfWork.SaveAsync();

            return Ok("Removed from wishlist");
        }

        // PATCH /api/Wishlist/items/{itemId}/rating — set or clear star rating
        [HttpPatch("items/{itemId}/rating")]
        public async Task<IActionResult> UpdateItemRating(Guid itemId, [FromBody] UpdateWishlistItemViewModel model)
        {
            var userId = GetUserId();
            var item   = await _unitOfWork.WishlistItems.GetByIdAsync(itemId);

            if (item == null) return NotFound("Wishlist item not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(item.WishlistId);
            if (wishlist == null || wishlist.OwnerId != userId) return Forbid();

            if (model.Rating.HasValue && (model.Rating < 1 || model.Rating > 5))
                return BadRequest("Rating must be between 1 and 5");

            item.Rating = model.Rating;
            _unitOfWork.WishlistItems.Update(item);
            await _unitOfWork.SaveAsync();

            return Ok(item);
        }

        // POST /api/Wishlist/collaborators — add collaborator by email (owner only)
        [HttpPost("collaborators")]
        public async Task<IActionResult> AddCollaborator([FromBody] AddCollaboratorViewModel model)
        {
            var userId   = GetUserId();
            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(model.WishlistId);

            if (wishlist == null)           return NotFound("Wishlist not found");
            if (wishlist.OwnerId != userId) return Forbid();
            if (!wishlist.IsShared)         return BadRequest("Enable sharing on this wishlist first.");

            var targetUser = await _userManager.FindByEmailAsync(model.Email);
            if (targetUser == null)         return NotFound("No user with that email found");
            if (targetUser.Id == userId)    return BadRequest("You cannot add yourself as a collaborator");

            var existing = (await _unitOfWork.WishlistCollaborators.FindAsync(x =>
                x.WishlistId == model.WishlistId && x.UserId == targetUser.Id)).FirstOrDefault();
            if (existing != null) return BadRequest("User is already a collaborator");

            var collaborator = new WishlistCollaborator
            {
                Id          = Guid.NewGuid(),
                WishlistId  = model.WishlistId,
                UserId      = targetUser.Id,
                AddedAt     = DateTime.UtcNow,
            };
            await _unitOfWork.WishlistCollaborators.AddAsync(collaborator);
            await _unitOfWork.SaveAsync();

            return Ok(new
            {
                collaborator.Id,
                collaborator.WishlistId,
                collaborator.UserId,
                collaborator.AddedAt,
                Name  = targetUser.Name,
                Email = targetUser.Email,
            });
        }

        // DELETE /api/Wishlist/collaborators/{id} — remove collaborator (owner only)
        [HttpDelete("collaborators/{id}")]
        public async Task<IActionResult> RemoveCollaborator(Guid id)
        {
            var userId       = GetUserId();
            var collaborator = await _unitOfWork.WishlistCollaborators.GetByIdAsync(id);

            if (collaborator == null) return NotFound("Collaborator not found");

            var wishlist = await _unitOfWork.Wishlists.GetByIdAsync(collaborator.WishlistId);
            if (wishlist == null || wishlist.OwnerId != userId) return Forbid();

            _unitOfWork.WishlistCollaborators.Delete(collaborator);
            await _unitOfWork.SaveAsync();

            return Ok("Collaborator removed");
        }
    }
}
