namespace DARI_API.ViewModels
{
    public class VisualSearchResultViewModel
    {
        public Guid ListingId { get; set; }
        public string Title { get; set; }
        public string ImageUrl { get; set; }
        public double SimilarityScore { get; set; }
    }
}
