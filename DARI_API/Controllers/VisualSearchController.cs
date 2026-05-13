using DARI_API.IServicesLayer;
using DARI_API.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DARI_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VisualSearchController : ControllerBase
    {
        private readonly IServiceLayer _visualSearchService;
        private readonly IUnitOfWork _unitOfWork;

        public VisualSearchController(IServiceLayer visualSearchService, IUnitOfWork unitOfWork)
        {
            _visualSearchService = visualSearchService;
            _unitOfWork = unitOfWork;
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromForm] VisualSearchRequestViewModel request, [FromQuery] int topN = 10)
        {
            if (request.Image == null || request.Image.Length == 0)
                return BadRequest("Please upload an image.");

            if (!request.Image.ContentType.StartsWith("image/"))
                return BadRequest("File must be an image.");

            if (request.Image.Length > 10 * 1024 * 1024)
                return BadRequest("Image must be under 10MB.");

            var results = await _visualSearchService.SearchAsync(request, topN);
            return Ok(results);
        }

        [HttpPost("index/{imageId}")]
        public async Task<IActionResult> IndexImage(Guid imageId)
        {
            var image = (await _unitOfWork.Images.FindAsync(i => i.Id == imageId)).FirstOrDefault();
            if (image == null) return NotFound("Image not found.");

            await _visualSearchService.IndexImageAsync(imageId, image.Url);
            return Ok(new { message = "Image indexed successfully." });
        }

        /// <summary>
        /// Wipe all stored embeddings and re-index every image. Use after a CV
        /// backbone change (e.g. ViT-B/32 → ViT-L/14) so the embedding dimension
        /// matches the current model.
        /// </summary>
        [HttpPost("reindex")]
        public async Task<IActionResult> Reindex()
        {
            var all = (await _unitOfWork.ImageEmbeddings.GetAllAsync()).ToList();
            foreach (var e in all) _unitOfWork.ImageEmbeddings.Delete(e);
            await _unitOfWork.SaveAsync();

            var images = (await _unitOfWork.Images.GetAllAsync()).ToList();
            int ok = 0, fail = 0;
            foreach (var img in images)
            {
                try { await _visualSearchService.IndexImageAsync(img.Id, img.Url); ok++; }
                catch { fail++; }
            }
            return Ok(new { wiped = all.Count, indexed = ok, failed = fail, totalImages = images.Count });
        }
    }
}
