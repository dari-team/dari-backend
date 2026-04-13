namespace DARI.Models
{
    public enum ListingStatus { Pending, Approved, Rejected }

    public class Listing
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public ListingStatus Status { get; set; } = ListingStatus.Pending;

        public Guid OwnerId { get; set; }
        public ApplicationUser Owner { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}