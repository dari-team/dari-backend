namespace DARI_API.ViewModels
{
    public class AIDescriptionRequestViewModel
    {
        public string Title { get; set; }
        public decimal Price { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public decimal AreaSize { get; set; }
        public string PropertyType { get; set; }
        public string ListingType { get; set; }
        public string? Finishing { get; set; }
        public string? Location { get; set; }
    }

    public class AIDescriptionResponseViewModel
    {
        public string Description { get; set; }
        public List<string> Tags { get; set; }
    }
}