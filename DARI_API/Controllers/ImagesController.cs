using DARI_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DARI_API.Controllers
{
    [Authorize(Roles = "Lister,Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class ImagesController : ControllerBase
    {
        private readonly ICloudinaryService _cloudinary;

        public ImagesController(ICloudinaryService cloudinary)
        {
            _cloudinary = cloudinary;
        }

        [HttpPost("sign-upload")]
        public IActionResult SignUpload()
        {
            var signed = _cloudinary.GetSignedUploadParams();
            return Ok(signed);
        }
    }
}
