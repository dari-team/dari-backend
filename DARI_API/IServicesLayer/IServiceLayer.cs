namespace DARI_API.IServicesLayer
{
    public interface IServiceLayer
    {
         Task SendEmailAsync(string to, string subject, string body);

    }
}
