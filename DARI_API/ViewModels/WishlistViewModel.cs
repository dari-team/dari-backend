namespace DARI_API.ViewModels
{
    // POST /api/Wishlist
    public class WishlistAddViewModel
    {
        public Guid? ListingId { get; set; }         // null = create empty wishlist only
        public Guid? WishlistId { get; set; }        // existing wishlist to add to
        public string? NewWishlistName { get; set; } // creates a new wishlist with this name
        public bool? IsShared { get; set; }          // initial shared state (new wishlist only)
    }

    // PATCH /api/Wishlist/{id}
    public class UpdateWishlistViewModel
    {
        public string? Name { get; set; }
        public bool? IsShared { get; set; }
    }

    // PATCH /api/Wishlist/items/{itemId}/rating
    public class UpdateWishlistItemViewModel
    {
        public decimal? Rating { get; set; }
    }

    // POST /api/Wishlist/collaborators
    public class AddCollaboratorViewModel
    {
        public string Email { get; set; } = string.Empty;
        public Guid WishlistId { get; set; }
    }
}
