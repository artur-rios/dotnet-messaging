using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// The recipient address is personal data. The service logs a pseudonymous reference to it — enough to correlate
/// the lines of one send — and never the address itself, neither in the message nor in a structured property.
/// </summary>
[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunLoggingTests : IDisposable
{
    private const string Recipient = "Jane.Doe@Example.com";

    private readonly RecordingLogger _logger = new();
    private readonly StubHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunLoggingTests()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "mg.example.com");

        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(_logger, _httpClient);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        _httpClient.Dispose();
    }

    private static string ExpectedReference()
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Recipient.Trim().ToLowerInvariant()));

        return Convert.ToHexStringLower(digest)[..12];
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GivenASend_WhenItIsLogged_ThenTheAddressIsNeverWritten(HttpStatusCode status)
    {
        _handler.Status = status;

        await _service.SendEmailAsync(Recipient, "s", "b");

        Assert.NotEmpty(_logger.Entries);

        foreach (var (message, values) in _logger.Entries)
        {
            Assert.DoesNotContain(Recipient, message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(values, value =>
                value?.ToString()?.Contains(Recipient, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GivenASend_WhenItIsLogged_ThenEveryLineCarriesTheSameReference(HttpStatusCode status)
    {
        _handler.Status = status;

        await _service.SendEmailAsync(Recipient, "s", "b");

        Assert.Equal(2, _logger.Entries.Count);
        Assert.All(_logger.Entries, entry => Assert.Contains(ExpectedReference(), entry.Message));
    }

    private sealed class RecordingLogger : ILogger<MailgunEmailService>
    {
        public ConcurrentQueue<(string Message, List<object?> Values)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(pair => pair.Value).ToList()
                : [];

            Entries.Enqueue((formatter(state, exception), values));
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("{\"message\": \"done\"}", Encoding.UTF8, "application/json")
            });
    }
}
