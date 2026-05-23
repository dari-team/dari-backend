using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace DARI_API.Models
{
    public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ==========================
        // DbSets
        // ==========================
        public DbSet<Listing> Listings { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<Image> Images { get; set; }
        public DbSet<Wishlist> Wishlists { get; set; }
        public DbSet<WishlistItem> WishlistItems { get; set; }
        public DbSet<WishlistCollaborator> WishlistCollaborators { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Inquiry> Inquiries { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<ListingView> ListingViews { get; set; }
        public DbSet<ImageEmbedding> ImageEmbeddings { get; set; }
        public DbSet<Complaint> Complaints { get; set; }
        // ==========================
        // Model Configuration
        // ==========================

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ==========================
            // Identity Table Naming (Optional Clean Names)
            // ==========================

            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
            builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
            builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
            builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
            builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
            builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");

            // ==========================
            // Unique Indexes
            // ==========================

            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.Email)
                .IsUnique();

            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            // ==========================
            // One-to-One Relationships
            // ==========================

            builder.Entity<Listing>()
                .HasOne(l => l.Address)
                .WithOne(a => a.Listing)
                .HasForeignKey<Address>(a => a.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

            // ==========================
            // Prevent Multiple Cascade Paths (Important!)
            // ==========================

            builder.Entity<Inquiry>()
                .HasOne(i => i.Customer)
                .WithMany(u => u.CustomerInquiries)
                .HasForeignKey(i => i.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Inquiry>()
                .HasOne(i => i.Lister)
                .WithMany(u => u.ListerInquiries)
                .HasForeignKey(i => i.ListerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Message>()
                .HasOne(m => m.Inquiry)
                .WithMany(i => i.Messages)
                .HasForeignKey(m => m.InquiryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Comment>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WishlistItem>()
                .HasOne(wi => wi.Wishlist)
                .WithMany(w => w.Items)
                .HasForeignKey(wi => wi.WishlistId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WishlistItem>()
                .HasOne(wi => wi.Listing)
                .WithMany()
                .HasForeignKey(wi => wi.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<WishlistCollaborator>()
                .HasOne(wc => wc.Wishlist)
                .WithMany(w => w.Collaborators)
                .HasForeignKey(wc => wc.WishlistId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WishlistCollaborator>()
                .HasOne(wc => wc.User)
                .WithMany()
                .HasForeignKey(wc => wc.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Comment>()
                .HasOne(c => c.WishlistItem)
                .WithMany(wi => wi.Comments)
                .HasForeignKey(c => c.WishlistItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Image>()
                .HasOne(i => i.Listing)
                .WithMany(l => l.Images)
                .HasForeignKey(i => i.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Inquiry>()
                .HasOne(i => i.Listing)
                .WithMany()
                .HasForeignKey(i => i.ListingId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ListingView>()
                .HasOne(lv => lv.Listing)
                .WithMany(l => l.Views)
                .HasForeignKey(lv => lv.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ListingView>()
                .HasOne(lv => lv.User)
                .WithMany(u => u.ListingViews)
                .HasForeignKey(lv => lv.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Back the 24h dedup lookups: "has this visitor viewed this listing recently?"
            builder.Entity<ListingView>()
                .HasIndex(lv => new { lv.ListingId, lv.VisitorHash, lv.ViewedAt });

            builder.Entity<ListingView>()
                .HasIndex(lv => new { lv.ListingId, lv.UserId, lv.ViewedAt });

            builder.Entity<ImageEmbedding>()
                .HasOne(e => e.Image)
                .WithOne(i => i.ImageEmbedding)
                .HasForeignKey<ImageEmbedding>(e => e.ImageId);

            builder.Entity<Complaint>()
                .HasOne(c => c.Listing)
                .WithMany(l => l.Complaints)
                .HasForeignKey(c => c.ListingId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Complaint>()
                .HasOne(c => c.Reporter)
                .WithMany()
                .HasForeignKey(c => c.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Human-friendly listing reference numbers come from a SQL sequence
            // starting at 100000000, so the first listing reads like "100000000".
            builder.HasSequence<int>("ListingReferenceSeq").StartsAt(100000000).IncrementsBy(1);
            builder.Entity<Listing>()
                .Property(l => l.ReferenceNumber)
                .HasDefaultValueSql("NEXT VALUE FOR ListingReferenceSeq");
            builder.Entity<Listing>()
                .HasIndex(l => l.ReferenceNumber)
                .IsUnique();
        }
    }
}
