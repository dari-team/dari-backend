using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace DARI_API.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly CloudinaryOptions _options;

        public CloudinaryService(IOptions<CloudinaryOptions> options)
        {
            _options = options.Value;
            var account = new Account(_options.CloudName, _options.ApiKey, _options.ApiSecret);
            _cloudinary = new Cloudinary(account);
        }

        public SignedUploadParams GetSignedUploadParams(string? publicIdPrefix = null)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Sign the params the client will send with the upload
            var paramsToSign = new SortedDictionary<string, object>
            {
                { "folder", _options.Folder },
                { "timestamp", timestamp }
            };

            var signature = _cloudinary.Api.SignParameters(paramsToSign);

            return new SignedUploadParams(
                CloudName: _options.CloudName,
                ApiKey: _options.ApiKey,
                Folder: _options.Folder,
                Timestamp: timestamp,
                Signature: signature,
                AllowedFormats: _options.AllowedFormats,
                MaxFileSizeBytes: _options.MaxFileSizeBytes
            );
        }

        public async Task<bool> VerifyUploadedAsync(string publicId)
        {
            try
            {
                var result = await _cloudinary.GetResourceAsync(new GetResourceParams(publicId));
                return result != null && !string.IsNullOrEmpty(result.PublicId);
            }
            catch
            {
                return false;
            }
        }

        public async Task DeleteAsync(string publicId)
        {
            await _cloudinary.DestroyAsync(new DeletionParams(publicId));
        }
    }
}
