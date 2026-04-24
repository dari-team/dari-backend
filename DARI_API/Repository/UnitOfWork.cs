using DARI_API.Models;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public IRepository<Listing> Listings { get; private set; }
    public IRepository<Address> Addresses { get; private set; }
    public IRepository<Image> Images { get; private set; }
    public IRepository<Wishlist> Wishlists { get; private set; }
    public IRepository<WishlistItem> WishlistItems { get; private set; }
    public IRepository<WishlistCollaborator> WishlistCollaborators { get; private set; }
    public IRepository<Comment> Comments { get; private set; }
    public IRepository<Inquiry> Inquiries { get; private set; }
    public IRepository<Message> Messages { get; private set; }
    public IRepository<Notification> Notifications { get; private set; }
    public IRepository<ImageEmbedding> ImageEmbeddings { get; private set; }

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;

        Listings = new Repository<Listing>(_context);
        Addresses = new Repository<Address>(_context);
        Images = new Repository<Image>(_context);
        Wishlists = new Repository<Wishlist>(_context);
        WishlistItems = new Repository<WishlistItem>(_context);
        WishlistCollaborators = new Repository<WishlistCollaborator>(_context);
        Comments = new Repository<Comment>(_context);
        Inquiries = new Repository<Inquiry>(_context);
        Messages = new Repository<Message>(_context);
        Notifications = new Repository<Notification>(_context);
        ImageEmbeddings = new Repository<ImageEmbedding>(_context);

    }

    public async Task<int> SaveAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}