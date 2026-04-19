namespace DARI_API.Services
{
    public class CloudinaryOptions
    {
        public string CloudName { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ApiSecret { get; set; } = "";
        public string Folder { get; set; } = "dari/listings";
        public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
        public string AllowedFormats { get; set; } = "jpg,jpeg,png,webp";
    }
}
