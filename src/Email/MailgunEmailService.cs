using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Output;
using Microsoft.Extensions.Logging;

namespace ArturRios.Messaging.Email;

/// <summary>
/// Sends transactional e-mails through the <see href="https://www.mailgun.com/">Mailgun</see> HTTP API.
/// </summary>
/// <remarks>
/// Configuration is read from environment variables at call time:
/// <c>MAILGUN_API_KEY</c>, <c>MAILGUN_DOMAIN</c>, and the optional <c>MAILGUN_FROM</c> (the sender; defaults to
/// <c>postmaster@</c> the domain) and <c>MAILGUN_API_VERSION</c>.
/// </remarks>
public class MailgunEmailService : IEmailService
{
    private const string MailgunApiBaseUrl = "https://api.mailgun.net";
    private const string MailgunMessagesEndpoint = "messages";

    /// <summary>
    /// The client used when the caller supplies none. One per process rather than one per service
    /// instance: a fresh <see cref="HttpClient"/> per instance holds its connections open after it goes
    /// out of scope, and nothing here ever disposed one.
    /// </summary>
    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient(), isThreadSafe: true);

    private readonly ILogger<MailgunEmailService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Name of the environment variable holding the Mailgun private API key.
    /// </summary>
    public const string ApiKeyVariable = "MAILGUN_API_KEY";

    /// <summary>
    /// Name of the environment variable holding the Mailgun sending domain.
    /// </summary>
    public const string DomainVariable = "MAILGUN_DOMAIN";

    /// <summary>
    /// Name of the environment variable holding the Mailgun API version to target.
    /// </summary>
    public const string ApiVersionVariable = "MAILGUN_API_VERSION";

    /// <summary>
    /// Name of the environment variable holding the sender (<c>from</c>) of every message: an address
    /// (<c>no-reply@example.com</c>) or a display name and an address (<c>Example &lt;no-reply@example.com&gt;</c>).
    /// When it is not set or is blank, messages are sent as <c>postmaster@</c> the <see cref="DomainVariable"/>
    /// domain, with no display name.
    /// </summary>
    public const string FromVariable = "MAILGUN_FROM";

    /// <summary>
    /// API version used when <see cref="ApiVersionVariable"/> is not set or is blank.
    /// </summary>
    public const string DefaultMailgunApiVersion = "v3";

    /// <summary>
    /// Initializes a new instance of the <see cref="MailgunEmailService"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record request and response details.</param>
    /// <param name="httpClient">
    /// HTTP client used to reach the Mailgun API. When <see langword="null"/>, a client shared by the
    /// whole process is used.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public MailgunEmailService(ILogger<MailgunEmailService> logger, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _httpClient = httpClient ?? SharedClient.Value;
    }

    /// <summary>
    /// Sends a plain text e-mail through the Mailgun API.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <returns>
    /// A <see cref="ProcessOutput"/> without errors when Mailgun accepts the message, or carrying the status code and
    /// response body when it does not. See <see cref="SendEmailAsync(string, string, string, CancellationToken)"/>.
    /// </returns>
    /// <exception cref="HttpRequestException">Mailgun could not be reached (DNS, connection or TLS failure).</exception>
    /// <exception cref="TaskCanceledException">The request exceeded the <see cref="HttpClient.Timeout"/>.</exception>
    public Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
        => SendEmailAsync(to, subject, body, CancellationToken.None);

    /// <summary>
    /// Sends a plain text e-mail through the Mailgun API, observing <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="to">Recipient e-mail address.</param>
    /// <param name="subject">Subject line of the e-mail.</param>
    /// <param name="body">Plain text body of the e-mail.</param>
    /// <param name="cancellationToken">Cancels the send, and the HTTP request it makes.</param>
    /// <returns>
    /// A <see cref="ProcessOutput"/> without errors when Mailgun accepts the message, or carrying the status code and
    /// response body when it does not. A missing API key or sending domain, or an invalid <see cref="FromVariable"/>
    /// sender, is reported the same way rather than sent to Mailgun.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Missing configuration and messages Mailgun rejects are reported in the returned envelope; failing to reach
    /// Mailgun at all is thrown, as is cancellation. An already-canceled token throws before the environment is read
    /// or a request built.
    /// </para>
    /// <para>
    /// The recipient is logged as a pseudonymous reference — the first twelve hex characters of the SHA-256 of the
    /// trimmed, lower-cased address — never as the address itself.
    /// </para>
    /// <para>
    /// The credential is attached to the request rather than to <see cref="HttpClient.DefaultRequestHeaders"/>.
    /// The client may be shared — the documented registration is <c>AddHttpClient</c>, which hands out a
    /// client this service does not own — and writing a credential onto its defaults both races with
    /// concurrent sends and leaves the Mailgun key attached to every later request that client makes.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="HttpRequestException">Mailgun could not be reached (DNS, connection or TLS failure).</exception>
    /// <exception cref="TaskCanceledException">The request exceeded the <see cref="HttpClient.Timeout"/>.</exception>
    public async Task<ProcessOutput> SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var output = new ProcessOutput();

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        var domain = Environment.GetEnvironmentVariable(DomainVariable);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.AddError($"Cannot send e-mail via Mailgun: the {ApiKeyVariable} environment variable is not set.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            output.AddError($"Cannot send e-mail via Mailgun: the {DomainVariable} environment variable is not set.");
        }

        var from = GetSender(domain);

        if (from is null)
        {
            output.AddError(
                $"Cannot send e-mail via Mailgun: the {FromVariable} environment variable is not a single e-mail " +
                "address, optionally with a display name (\"Example <no-reply@example.com>\").");
        }

        if (!output.Success)
        {
            _logger.LogError("Mailgun is not configured: {Errors}", string.Join(" ", output.Errors));

            return output;
        }

        var apiVersion = GetApiVersion();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{MailgunApiBaseUrl}/{apiVersion}/{domain}/{MailgunMessagesEndpoint}")
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{apiKey}")))
            },
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("from", from!),
                new KeyValuePair<string, string>("to", to),
                new KeyValuePair<string, string>("subject", subject),
                new KeyValuePair<string, string>("text", body)
            ])
        };

        var recipientRef = RecipientReference(to);

        _logger.LogInformation("Sending e-mail to {RecipientRef} via Mailgun...", recipientRef);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Mailgun accepted the message for {RecipientRef}.", recipientRef);

            return output;
        }

        _logger.LogError("Mailgun rejected the message for {RecipientRef}: {StatusCode} | {ResponseContent}",
            recipientRef, response.StatusCode, responseContent);

        output.AddError(
            $"Failed to send e-mail via Mailgun. Status Code: {response.StatusCode} | Response: {responseContent}");

        return output;
    }

    /// <summary>
    /// A stable, non-reversible reference to a recipient address, so log lines can be correlated without writing
    /// the address — personal data — to the log.
    /// </summary>
    /// <param name="address">The recipient address.</param>
    /// <returns>
    /// The first twelve hex characters of the SHA-256 of the trimmed, lower-cased address, or <c>(none)</c> when
    /// there is no address.
    /// </returns>
    private static string RecipientReference(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "(none)";
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant()));

        return Convert.ToHexStringLower(digest)[..12];
    }

    /// <summary>
    /// Reads the sender from <see cref="FromVariable"/>.
    /// </summary>
    /// <param name="domain">The sending domain, for the default sender.</param>
    /// <returns>
    /// The trimmed configured sender when it is one valid address, with or without a display name;
    /// <c>postmaster@<paramref name="domain"/></c> when it is unset or blank; <see langword="null"/> when it is set
    /// but invalid.
    /// </returns>
    private static string? GetSender(string? domain)
    {
        var from = Environment.GetEnvironmentVariable(FromVariable)?.Trim();

        if (string.IsNullOrEmpty(from))
        {
            return $"postmaster@{domain}";
        }

        return IsSingleMailbox(from) ? from : null;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is exactly one mailbox: a bare address, or a display name followed by an
    /// address in angle brackets. Line breaks are refused by <see cref="MailAddress"/> itself, so a header cannot be
    /// smuggled into the value.
    /// </summary>
    /// <remarks>
    /// <see cref="MailAddress"/> alone is not enough: it reads <c>a@x.com, b@x.com</c> as the address
    /// <c>b@x.com</c> with the display name <c>a@x.com,</c>. So the parsed address must account for the whole value,
    /// and an unquoted display name must not carry what would make it a list or another address.
    /// </remarks>
    private static bool IsSingleMailbox(string value)
    {
        if (!MailAddress.TryCreate(value, out var mailbox))
        {
            return false;
        }

        if (string.Equals(value, mailbox.Address, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bracketed = $"<{mailbox.Address}>";

        if (!value.EndsWith(bracketed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var displayName = value[..^bracketed.Length].TrimEnd();

        if (displayName.Length >= 2 && displayName[0] == '"' && displayName[^1] == '"')
        {
            return true;
        }

        return displayName.Length > 0 && displayName.IndexOfAny([',', ';', '@', '<', '>', '"']) < 0;
    }

    /// <summary>
    /// Reads the Mailgun API version from the environment.
    /// </summary>
    /// <returns>
    /// The value of <see cref="ApiVersionVariable"/>, or <see cref="DefaultMailgunApiVersion"/> when it is unset or blank.
    /// </returns>
    private static string GetApiVersion()
    {
        var apiVersion = Environment.GetEnvironmentVariable(ApiVersionVariable);

        return string.IsNullOrWhiteSpace(apiVersion) ? DefaultMailgunApiVersion : apiVersion;
    }
}
