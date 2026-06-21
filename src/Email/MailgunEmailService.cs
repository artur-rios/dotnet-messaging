using System.Net.Http.Headers;
using System.Text;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Messaging.Email;

public class MailgunEmailService : IEmailService
{
    private readonly ILogger<MailgunEmailService> _logger;
    private readonly HttpClient _httpClient;
    private const string MailgunApiBaseUrl = "https://api.mailgun.net";
    private const string MailgunApiVersion = "v3";
    private const string MailgunMessagesEndpoint = "messages";

    public MailgunEmailService(ILogger<MailgunEmailService> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
    {
        var apiKey = Environment.GetEnvironmentVariable("MAILGUN_API_KEY");
        var domain = Environment.GetEnvironmentVariable("MAILGUN_DOMAIN");

        var byteArray = Encoding.ASCII.GetBytes($"api:{apiKey}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("from", $"Mailgun Sandbox <postmaster@{domain}>"),
            new KeyValuePair<string, string>("to", to),
            new KeyValuePair<string, string>("subject", subject),
            new KeyValuePair<string, string>("text", body)
        ]);

        _logger.LogInformation("Testing Mailgun email service...");

        var response =
            await _httpClient.PostAsync($"{MailgunApiBaseUrl}/{MailgunApiVersion}/{domain}/{MailgunMessagesEndpoint}",
                content);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Mailgun response: {ResponseContent}", responseContent);

        var output = new ProcessOutput();

        if (!response.IsSuccessStatusCode)
        {
            output.AddError(
                $"Failed to send e-mail via Mailgun. Status Code: {response.StatusCode} | Response: {responseContent}");
        }

        return output;
    }
}
