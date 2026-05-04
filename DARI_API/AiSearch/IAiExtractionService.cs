namespace DARI_API.AiSearch;

public interface IAiExtractionService
{
    Task<AiExtractionResult> ExtractAsync(string userQuery, CancellationToken ct = default);
}
