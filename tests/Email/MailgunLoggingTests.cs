using System.Collections.Concurrent;
using System.Globalization;
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

    private readonly MailgunEnvironment _environment = new(apiKey: "key", domain: "mg.example.com");
    private readonly RecordingLogger _logger = new();
    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunLoggingTests()
    {
        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(_logger, _httpClient);
    }

    public void Dispose()
    {
        _environment.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
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
        _handler.SetResponse(status, "{\"message\": \"done\"}");

        await _service.SendEmailAsync(Recipient, "s", "b", TestContext.Current.CancellationToken);

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
        _handler.SetResponse(status, "{\"message\": \"done\"}");

        await _service.SendEmailAsync(Recipient, "s", "b", TestContext.Current.CancellationToken);

        Assert.Equal(2, _logger.Entries.Count);
        Assert.All(_logger.Entries, entry => Assert.Contains(ExpectedReference(), entry.Message));
    }

    [Fact]
    public async Task GivenMissingConfiguration_WhenSending_ThenOneErrorNamesItWithoutTheAddress()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);

        await _service.SendEmailAsync(Recipient, "s", "b", TestContext.Current.CancellationToken);

        var (message, values) = Assert.Single(_logger.Entries);
        Assert.Contains(MailgunEmailService.ApiKeyVariable, message);
        Assert.DoesNotContain(Recipient, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(values, value =>
            value?.ToString()?.Contains(Recipient, StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankRecipient_WhenItIsLogged_ThenTheReferenceSaysThereIsNone(string recipient)
    {
        await _service.SendEmailAsync(recipient, "s", "b", TestContext.Current.CancellationToken);

        Assert.All(_logger.Entries, entry => Assert.Contains("(none)", entry.Message));
    }

    [Fact]
    public async Task GivenTheSameAddressSpelledTwoWays_WhenEachIsLogged_ThenBothCarryTheSameReference()
    {
        await _service.SendEmailAsync("  JANE.DOE@example.COM ", "s", "b", TestContext.Current.CancellationToken);

        Assert.All(_logger.Entries, entry => Assert.Contains(ExpectedReference(), entry.Message));
    }

    [Fact]
    public async Task GivenACultureWithItsOwnCasing_WhenTheRecipientIsLogged_ThenTheReferenceIsTheInvariantOne()
    {
        // Turkish lower-cases I to a dotless i, so a culture-sensitive reference would change with the machine.
        const string recipient = "INFO@EXAMPLE.COM";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("info@example.com")))[..12];
        var previous = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            await _service.SendEmailAsync(recipient, "s", "b", TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.All(_logger.Entries, entry => Assert.Contains(expected, entry.Message));
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
}
