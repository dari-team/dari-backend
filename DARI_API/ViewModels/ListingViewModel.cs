using DARI_API.Models;

namespace DARI_API.ViewModels
{
    public class ListingViewModel
    {
        public string title { get; set; }
        public decimal price { get; set; }
        public string description { get; set; }
        public Guid ListerId { get; set; }
        public int bedrooms { get; set; }
        public int bathrooms { get; set; } 
        public decimal areaSize { get; set; }
        public PropertyType propertyType { get; set; }
        public string? finishing { get; set; }
        public ListingType listingType { get; set; }


    }

}
