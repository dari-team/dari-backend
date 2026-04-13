using DARI_API.IServicesLayer;
using System.Net.Mail;
using System.Net;

namespace DARI_API.ServicesLayer
{
    public class ServiceLayer : IServiceLayer
    {
        private readonly IConfiguration _configuration;

        public ServiceLayer(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var email = _configuration["EmailSettings:Email"];
            var password = _configuration["EmailSettings:Password"];

            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(email, password),
                EnableSsl = true
            };

            var mail = new MailMessage(email, to, subject, body);

            await client.SendMailAsync(mail);
        }
    }
}
