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

            var results = await _visualSearchService.SearchAsync(request.Image, topN);
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
    }
}
