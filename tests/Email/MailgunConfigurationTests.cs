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
    private readonly MailgunEnvironment _environment = new();
    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunConfigurationTests()
    {
        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);
    }

    public void Dispose()
    {
        _environment.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GivenNoApiKey_WhenSendingAnEmail_ThenTheEnvelopeSaysSoAndNothingIsSent()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.ApiKeyVariable));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GivenNoDomain_WhenSendingAnEmail_ThenTheEnvelopeSaysSoAndNothingIsSent()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.DomainVariable));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GivenNeitherApiKeyNorDomain_WhenSendingAnEmail_ThenBothAreReported()
    {
        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Equal(2, output.Errors.Count);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankApiKey_WhenSendingAnEmail_ThenItIsTreatedAsMissing(string apiKey)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, apiKey);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Empty(_handler.Requests);
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

        await _service.SendEmailAsync("to@example.com", "Hello", "World", TestContext.Current.CancellationToken);
        await _service.SendEmailAsync("to@example.com", "Hello", "World", TestContext.Current.CancellationToken);

        Assert.Null(_httpClient.DefaultRequestHeaders.Authorization);
        Assert.Equal(2, _handler.Requests.Count);
        Assert.All(_handler.Requests, request => Assert.Equal("Basic", request.AuthorizationScheme));
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankDomain_WhenSendingAnEmail_ThenItIsTreatedAsMissing(string domain)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, domain);

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.DomainVariable));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GivenNoApiKeyAndAnInvalidSender_WhenSendingAnEmail_ThenBothAreReported()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.mailgun.org");
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, "not-an-address");

        var output = await _service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, output.Errors.Count);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.ApiKeyVariable));
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.FromVariable));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GivenNoHttpClient_WhenConstructed_ThenTheServiceWorksOnTheSharedClient()
    {
        // Nothing is configured, so the shared client is never asked to reach Mailgun.
        var service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance);

        var output = await service.SendEmailAsync("to@example.com", "Hello", "World",
            TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.Equal(2, output.Errors.Count);
    }
}
