using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

public class MailgunEmailServiceTests : IDisposable
{
    private const string TestApiKey = "test-api-key";
    private const string TestDomain = "sandbox.mailgun.org";
    private const string TestApiVersion = "v4";
    private const string TestTo = "recipient@example.com";
    private const string TestSubject = "Test Subject";
    private const string TestBody = "Test email body";

    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _sut;

    public MailgunEmailServiceTests()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, TestApiKey);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, TestDomain);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);

        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _sut = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GivenMailgunReturnsSuccess_WhenSendEmailAsyncIsCalled_ThenOutputSuccessIsTrue()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{\"id\": \"<message-id>\", \"message\": \"Queued.\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        Assert.True(output.Success);
        Assert.Empty(output.Errors);
    }

    [Fact]
    public async Task GivenMailgunReturnsFailure_WhenSendEmailAsyncIsCalled_ThenOutputContainsError()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, "{\"message\": \"Forbidden\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        Assert.False(output.Success);
        Assert.NotEmpty(output.Errors);
    }

    [Fact]
    public async Task GivenMailgunReturnsFailure_WhenSendEmailAsyncIsCalled_ThenErrorMessageContainsStatusCode()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, "{\"message\": \"Bad Request\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        Assert.Contains(output.Errors, e => e.Contains("BadRequest"));
    }

    [Fact]
    public async Task GivenApiKeyConfigured_WhenSendEmailAsyncIsCalled_ThenRequestHasBasicAuthorizationHeader()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var authHeader = _mockHandler.LastRequest!.Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Basic", authHeader.Scheme);

        var expectedCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{TestApiKey}"));
        Assert.Equal(expectedCredentials, authHeader.Parameter);
    }

    [Fact]
    public async Task GivenDomainConfigured_WhenSendEmailAsyncIsCalled_ThenRequestSentToCorrectMailgunEndpoint()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var expectedUrl =
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenApiVersionConfigured_WhenSendEmailAsyncIsCalled_ThenRequestUsesConfiguredApiVersion()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, TestApiVersion);
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var expectedUrl = $"https://api.mailgun.net/{TestApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenApiVersionNotConfigured_WhenSendEmailAsyncIsCalled_ThenRequestUsesDefaultApiVersion()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var expectedUrl =
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenApiVersionIsBlank_WhenSendEmailAsyncIsCalled_ThenRequestUsesDefaultApiVersion(
        string apiVersion)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, apiVersion);
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var expectedUrl =
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenEmailData_WhenSendEmailAsyncIsCalled_ThenRequestBodyContainsToSubjectAndText()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var body = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync();
        var decoded = WebUtility.UrlDecode(body);
        Assert.Contains($"to={TestTo}", decoded);
        Assert.Contains($"subject={TestSubject}", decoded);
        Assert.Contains($"text={TestBody}", decoded);
    }

    [Fact]
    public async Task GivenDomainConfigured_WhenSendEmailAsyncIsCalled_ThenRequestFromFieldContainsDomain()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);

        var body = await _mockHandler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains(TestDomain, body);
    }
}

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseContent = "{}";

    public HttpRequestMessage? LastRequest { get; private set; }

    public void SetResponse(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _responseContent = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
