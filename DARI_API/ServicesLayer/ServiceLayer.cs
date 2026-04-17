using DARI_API.IServicesLayer;
using System.Net.Mail;
using System.Net;

namespace DARI_API.ServicesLayer
{
    public class ServiceLayer : IServiceLayer
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<ServiceLayer> _logger;

        public ServiceLayer(IConfiguration configuration, ILogger<ServiceLayer> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }
        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var email = _configuration["EmailSettings:Email"];
            var password = _configuration["EmailSettings:Password"];

            // Dev fallback: if SMTP credentials aren't configured, log the email
            // to the console instead of crashing. This lets local development
            // proceed without a Gmail app password.
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning(
                    "EmailSettings not configured. Logging email instead of sending.\n" +
                    "  To:      {To}\n" +
                    "  Subject: {Subject}\n" +
                    "  Body:    {Body}",
                    to, subject, body);
                return;
            }

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
