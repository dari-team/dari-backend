using DARI_API.Models;

public interface IUnitOfWork : IDisposable
{
    IRepository<Listing> Listings { get; }
    IRepository<Address> Addresses { get; }
    IRepository<Image> Images { get; }
    IRepository<Wishlist> Wishlists { get; }
    IRepository<WishlistItem> WishlistItems { get; }
    IRepository<WishlistCollaborator> WishlistCollaborators { get; }
    IRepository<Comment> Comments { get; }
    IRepository<Inquiry> Inquiries { get; }
    IRepository<Message> Messages { get; }
    IRepository<Notification> Notifications { get; }
    IRepository<ImageEmbedding> ImageEmbeddings { get; }

    Task<int> SaveAsync();
}