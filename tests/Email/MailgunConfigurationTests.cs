using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// Missing configuration is reported on the envelope rather than sent to Mailgun as an unauthenticated
/// request against an empty domain.
/// </summary>
[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunConfigurationTests : IDisposable
{
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunConfigurationTests()
    {
        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);

        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);

        _httpClient.Dispose();
        _handler.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GivenNoApiKey_WhenSendingAnEmail_ThenTheEnvelopeSaysSoAndNothingIsSent()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World");

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.ApiKeyVariable));
        Assert.Equal(0, _handler.RequestCount);
    }

    [Fact]
    public async Task GivenNoDomain_WhenSendingAnEmail_ThenTheEnvelopeSaysSoAndNothingIsSent()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World");

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.DomainVariable));
        Assert.Equal(0, _handler.RequestCount);
    }

    [Fact]
    public async Task GivenNeitherApiKeyNorDomain_WhenSendingAnEmail_ThenBothAreReported()
    {
        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World");

        Assert.False(output.Success);
        Assert.Equal(2, output.Errors.Count);
        Assert.Equal(0, _handler.RequestCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankApiKey_WhenSendingAnEmail_ThenItIsTreatedAsMissing(string apiKey)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, apiKey);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World");

        Assert.False(output.Success);
        Assert.Equal(0, _handler.RequestCount);
    }

    [Fact]
    public void GivenNoLogger_WhenConstructing_ThenArgumentNullExceptionIsThrown()
    {
        Assert.Throws<ArgumentNullException>(() => new MailgunEmailService(null!));
    }

    [Fact]
    public async Task GivenAConfiguredService_WhenSendingTwice_ThenTheClientDefaultHeadersAreNeverTouched()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");

        await _service.SendEmailAsync("to@example.com", "Hello", "World");
        await _service.SendEmailAsync("to@example.com", "Hello", "World");

        Assert.Null(_httpClient.DefaultRequestHeaders.Authorization);
        Assert.Equal(2, _handler.RequestCount);
        Assert.All(_handler.AuthorizationHeaders, header => Assert.Equal("Basic", header));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string> _authorizationHeaders = [];

        public int RequestCount { get; private set; }

        public IReadOnlyList<string> AuthorizationHeaders => _authorizationHeaders;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (request.Headers.Authorization?.Scheme is { } scheme)
            {
                _authorizationHeaders.Add(scheme);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
