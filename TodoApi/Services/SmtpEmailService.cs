using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using TodoApi.Models;

namespace TodoApi.Services
{
  public class SmtpEmailService : IEmailService
  {
    private readonly SmtpSettings _smtpSettings;
    public SmtpEmailService(IOptions<SmtpSettings> options)
    {
      _smtpSettings = options.Value;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
      var message = new MimeMessage();
      message.From.Add(new MailboxAddress("TodoApi", _smtpSettings.FromEmail));
      message.To.Add(new MailboxAddress(toEmail, toEmail));
      message.Subject = subject;
      message.Body = new TextPart("plain") { Text = body };

      using var client = new SmtpClient();
      await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, _smtpSettings.UseSSL);
      await client.AuthenticateAsync(_smtpSettings.UserName, _smtpSettings.Password);
      await client.SendAsync(message);
      await client.DisconnectAsync(true);
    }
  }
}
