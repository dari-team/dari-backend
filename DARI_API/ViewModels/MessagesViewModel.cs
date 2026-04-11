namespace DARI_API.ViewModels
{
    public class MessageViewModel
    {
        public Guid InquiryId { get; set; }
        public Guid SenderId { get; set; }
        public string Text { get; set; }
    }
}