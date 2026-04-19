namespace DARI_API.Services
{
    public interface ICloudinaryService
    {
        SignedUploadParams GetSignedUploadParams(string? publicIdPrefix = null);
        Task<bool> VerifyUploadedAsync(string publicId);
        Task DeleteAsync(string publicId);
    }

    public record SignedUploadParams(
        string CloudName,
        string ApiKey,
        string Folder,
        long Timestamp,
        string Signature,
        string AllowedFormats,
        long MaxFileSizeBytes
    );
}
