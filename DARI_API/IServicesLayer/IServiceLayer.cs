using DARI_API.ViewModels;

namespace DARI_API.IServicesLayer
{
    public interface IServiceLayer
    {
        Task SendEmailAsync(string to, string subject, string body);
        Task IndexImageAsync(Guid imageId, string imageUrl);
        Task<List<VisualSearchResultViewModel>> SearchAsync(VisualSearchRequestViewModel request, int topN = 10);
    }

}

