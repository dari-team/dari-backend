namespace DARI_API.ViewModels
{
    public class VisualSearchRequestViewModel
    {
        public IFormFile Image { get; set; }

        // Optional metadata pre-filters. If set, the visual search will only score
        // listings that already match these structured constraints. This drops
        // adjacent-room confusion (a kitchen-style dining room from a different
        // property type can't outrank a real kitchen in the same property type).
        public string? PropertyType { get; set; }   // "apartment" | "villa" | ...
        public string? ListingType  { get; set; }   // "buy" | "rent"
        public string? City         { get; set; }
        public int?    BedsMin      { get; set; }
        public int?    BedsMax      { get; set; }
    }
}
