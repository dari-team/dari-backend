namespace DARI_API.ViewModels
    {
        public class UpdateWishlistViewModel
        {
            public string? Name { get; set; }
            public bool? IsShared { get; set; }
        }

        public class UpdateWishlistItemViewModel
        {
            public decimal? Rating { get; set; } 
        }

        public class AddCollaboratorViewModel
        {
            public string Email { get; set; }
            public Guid WishlistId { get; set; }
        }

        public class WishlistAddViewModel
        {
            public Guid ListingId { get; set; }
            public Guid? WishlistId { get; set; }       
            public string? NewWishlistName { get; set; }
        }
}