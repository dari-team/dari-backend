using Microsoft.AspNetCore.Mvc;


namespace DARI_API.Controllers
{
    [ApiController]
    [Route("Api/[controller]")]
    public class ImageController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;

        public ImageController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteImage(Guid id)
        {
            var image = await _unitOfWork.Images.GetByIdAsync(id);

            if (image == null)
                return NotFound("Image not found");

            var path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                image.Url.TrimStart('/')
            );

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            _unitOfWork.Images.Delete(image);
            await _unitOfWork.SaveAsync();

            return Ok("Image deleted successfully");
        }
    }
}