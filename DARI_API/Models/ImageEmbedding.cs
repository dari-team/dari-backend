namespace DARI_API.Models
{
    public class ImageEmbedding
    {
        public Guid Id { get; set; }

        public Guid ImageId { get; set; }

        public Image Image { get; set; }
        
        public string EmbeddingJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
