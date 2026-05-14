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

        // Deletes Cloudinary assets that were uploaded from the Add Listing form
        // but never attached to a saved listing (the lister removed a photo or
        // abandoned the form). Best-effort: a failed delete on one publicId
        // never fails the whole request.
        [HttpPost("cleanup")]
        public async Task<IActionResult> Cleanup([FromBody] CleanupImagesRequest req)
        {
            if (req?.PublicIds == null || req.PublicIds.Count == 0)
                return Ok(new { deleted = 0 });

            var deleted = 0;
            foreach (var publicId in req.PublicIds)
            {
                if (string.IsNullOrWhiteSpace(publicId)) continue;
                try
                {
                    await _cloudinary.DeleteAsync(publicId);
                    deleted++;
                }
                catch
                {
                    // best-effort — keep going on the rest
                }
            }
            return Ok(new { deleted });
        }
    }

    public class CleanupImagesRequest
    {
        public List<string> PublicIds { get; set; } = new();
    }
}
