using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace AmarTools.Voting.Services.Email
{
    /// <summary>SMTP settings, bound from the "Email" configuration section.</summary>
    public sealed class EmailOptions
    {
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string FromAddress { get; set; } = "no-reply@amartools.local";
        public string FromName { get; set; } = "AmarTools Voting";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
    }

    /// <summary>
    /// Sends account emails (confirmation, password reset) over SMTP. Works with Gmail
    /// (smtp.gmail.com + app password), SendGrid (smtp.sendgrid.net, user "apikey") or
    /// Mailgun. When Email:SmtpHost is not set, the message is written to the log
    /// instead, so confirmation and reset links can be used during local development.
    /// </summary>
    public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
    {
        private readonly EmailOptions _options = options.Value;

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (!_options.IsConfigured)
            {
                logger.LogWarning(
                    "Email:SmtpHost is not configured; email NOT sent. To: {To} | Subject: {Subject} | Body: {Body}",
                    email, subject, htmlMessage);
                return;
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            message.To.Add(email);

            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.EnableSsl,
                Credentials = string.IsNullOrEmpty(_options.Username)
                    ? null
                    : new NetworkCredential(_options.Username, _options.Password)
            };

            await client.SendMailAsync(message);
            logger.LogInformation("Email '{Subject}' sent to {To}.", subject, email);
        }
    }
}
