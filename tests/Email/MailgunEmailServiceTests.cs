using System.Net;
using System.Text;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunEmailServiceTests : IDisposable
{
    private const string TestApiKey = "test-api-key";
    private const string TestDomain = "sandbox.mailgun.org";
    private const string TestApiVersion = "v4";
    private const string TestTo = "recipient@example.com";
    private const string TestSubject = "Test Subject";
    private const string TestBody = "Test email body";

    private readonly MailgunEnvironment _environment = new(apiKey: TestApiKey, domain: TestDomain);
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _sut;

    public MailgunEmailServiceTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _sut = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);
    }

    public void Dispose()
    {
        _environment.Dispose();
        _httpClient.Dispose();
        _mockHandler.Dispose();
    }

    [Fact]
    public async Task GivenMailgunReturnsSuccess_WhenSendEmailAsyncIsCalled_ThenOutputSuccessIsTrue()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{\"id\": \"<message-id>\", \"message\": \"Queued.\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        Assert.True(output.Success);
        Assert.Empty(output.Errors);
    }

    [Fact]
    public async Task GivenMailgunReturnsFailure_WhenSendEmailAsyncIsCalled_ThenOutputContainsError()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized, "{\"message\": \"Forbidden\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        Assert.False(output.Success);
        Assert.NotEmpty(output.Errors);
    }

    [Fact]
    public async Task GivenMailgunReturnsFailure_WhenSendEmailAsyncIsCalled_ThenErrorMessageContainsStatusCode()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, "{\"message\": \"Bad Request\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        Assert.Contains(output.Errors, e => e.Contains("BadRequest"));
    }

    [Fact]
    public async Task GivenApiKeyConfigured_WhenSendEmailAsyncIsCalled_ThenRequestHasBasicAuthorizationHeader()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

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

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        var expectedUrl =
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenApiVersionConfigured_WhenSendEmailAsyncIsCalled_ThenRequestUsesConfiguredApiVersion()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, TestApiVersion);
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        var expectedUrl = $"https://api.mailgun.net/{TestApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenApiVersionNotConfigured_WhenSendEmailAsyncIsCalled_ThenRequestUsesDefaultApiVersion()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

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

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        var expectedUrl =
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages";
        Assert.Equal(expectedUrl, _mockHandler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GivenEmailData_WhenSendEmailAsyncIsCalled_ThenRequestBodyContainsToSubjectAndText()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        var decoded = WebUtility.UrlDecode(_mockHandler.LastRequestBody!);
        Assert.Contains($"to={TestTo}", decoded);
        Assert.Contains($"subject={TestSubject}", decoded);
        Assert.Contains($"text={TestBody}", decoded);
    }

    [Fact]
    public async Task GivenDomainConfigured_WhenSendEmailAsyncIsCalled_ThenRequestFromFieldContainsDomain()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        Assert.Contains(TestDomain, _mockHandler.LastRequestBody!);
    }

    [Fact]
    public async Task GivenTheOverloadWithoutAToken_WhenSendEmailAsyncIsCalled_ThenItSendsTheSameRequest()
    {
        _mockHandler.SetResponse(HttpStatusCode.OK, "{}");

        // The overload without a token is the one under test, so the test's own token is not passed.
#pragma warning disable xUnit1051
        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody);
#pragma warning restore xUnit1051

        Assert.True(output.Success);
        Assert.Equal(
            $"https://api.mailgun.net/{MailgunEmailService.DefaultMailgunApiVersion}/{TestDomain}/messages",
            _mockHandler.LastRequest!.RequestUri!.ToString());

        var decoded = WebUtility.UrlDecode(_mockHandler.LastRequestBody!);
        Assert.Contains($"to={TestTo}", decoded);
        Assert.Contains($"subject={TestSubject}", decoded);
        Assert.Contains($"text={TestBody}", decoded);
    }
    [Fact]
    public async Task GivenMailgunReturnsFailure_WhenSendEmailAsyncIsCalled_ThenTheErrorCarriesTheResponseBody()
    {
        _mockHandler.SetResponse(HttpStatusCode.BadRequest, "{\"message\": \"'to' parameter is not a valid address\"}");

        var output = await _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken);

        var error = Assert.Single(output.Errors);
        Assert.Contains("'to' parameter is not a valid address", error);
    }

    [Fact]
    public async Task GivenMailgunCannotBeReached_WhenSendEmailAsyncIsCalled_ThenTheHttpRequestExceptionIsThrown()
    {
        var unreachable = new HttpRequestException("No such host is known.");
        _mockHandler.OnRequest = _ => throw unreachable;

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _sut.SendEmailAsync(TestTo, TestSubject, TestBody, TestContext.Current.CancellationToken));

        Assert.Same(unreachable, thrown);
    }

    [Fact]
    public async Task GivenOverlappingSends_WhenSentThroughOneClient_ThenEachRequestCarriesItsOwnRecipientAndCredential()
    {
        const int sends = 8;
        var allArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        // Every request waits until all of them have reached the client, so they are all in flight at once.
        _mockHandler.OnRequest = async cancellationToken =>
        {
            if (Interlocked.Increment(ref arrived) == sends)
            {
                allArrived.SetResult();
            }

            // The limit only turns a send that blocks the next one into a failure instead of a hang.
            await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        };

        var recipients = Enumerable.Range(0, sends).Select(index => $"recipient{index}@example.com").ToList();
        var outputs = await Task.WhenAll(recipients.Select(recipient =>
            _sut.SendEmailAsync(recipient, TestSubject, TestBody, TestContext.Current.CancellationToken)));

        Assert.All(outputs, output => Assert.True(output.Success));
        var expectedCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"api:{TestApiKey}"));
        Assert.All(_mockHandler.Requests, request => Assert.Equal(expectedCredentials, request.AuthorizationParameter));
        Assert.Equal(
            recipients.Order(),
            _mockHandler.Requests.Select(request => SentField(request.Body!, "to")).Order());
        Assert.Null(_httpClient.DefaultRequestHeaders.Authorization);
    }

    private static string? SentField(string body, string name) =>
        body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(parts => WebUtility.UrlDecode(parts[0]) == name)
            .Select(parts => WebUtility.UrlDecode(parts[1]))
            .SingleOrDefault();
}
