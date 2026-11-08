using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ArturRios.Messaging.Email;
using ArturRios.Output;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// A message can carry its own sender and reply-to, for a caller that sends on behalf of several projects. A message
/// that sets neither behaves exactly as the three-argument overload does.
/// </summary>
[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunMessageOptionsTests : IDisposable
{
    private const string Domain = "mg.example.com";
    private const string To = "to@example.com";

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunMessageOptionsTests()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, Domain);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, "Configured <configured@example.com>");

        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, null);

        _httpClient.Dispose();
        _handler.Dispose();

        GC.SuppressFinalize(this);
    }

    private string? SentField(string name)
    {
        foreach (var pair in _handler.LastRequestBody!.Split('&'))
        {
            var parts = pair.Split('=', 2);

            if (WebUtility.UrlDecode(parts[0]) == name)
            {
                return WebUtility.UrlDecode(parts[1]);
            }
        }

        return null;
    }

    private static EmailMessage Message() => new(To, "Subject", "Body");

    [Fact]
    public async Task GivenAMessageWithASender_WhenSent_ThenFromIsThatSenderAndNotMailgunFrom()
    {
        var output = await _service.SendEmailAsync(Message() with { From = "Client <client@mg.example.com>" });

        Assert.True(output.Success);
        Assert.Equal("Client <client@mg.example.com>", SentField("from"));
    }

    [Fact]
    public async Task GivenAMessageWithoutASender_WhenSent_ThenFromFollowsMailgunFrom()
    {
        var output = await _service.SendEmailAsync(Message());

        Assert.True(output.Success);
        Assert.Equal("Configured <configured@example.com>", SentField("from"));
    }

    [Fact]
    public async Task GivenAMessageWithAReplyTo_WhenSent_ThenTheReplyToHeaderFieldIsSet()
    {
        var output = await _service.SendEmailAsync(Message() with { ReplyTo = "visitor@example.com" });

        Assert.True(output.Success);
        Assert.Equal("visitor@example.com", SentField("h:Reply-To"));
    }

    [Fact]
    public async Task GivenAMessageWithAReplyTo_WhenSent_ThenTheRecipientIsUnchanged()
    {
        await _service.SendEmailAsync(Message() with { ReplyTo = "visitor@example.com" });

        Assert.Equal(To, SentField("to"));
    }

    [Fact]
    public async Task GivenAMessageWithoutAReplyTo_WhenSent_ThenNoReplyToFieldIsSent()
    {
        await _service.SendEmailAsync(Message());

        Assert.Null(SentField("h:Reply-To"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("a@example.com\r\nBcc: c@example.com")]
    public async Task GivenAnInvalidSender_WhenSent_ThenFailsAndSendsNothing(string sender)
    {
        var output = await _service.SendEmailAsync(Message() with { From = sender });

        Assert.False(output.Success);
        Assert.Null(_handler.LastRequest);
        Assert.Contains(output.Errors, e => e.Contains("sender"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("a@example.com\r\nBcc: c@example.com")]
    public async Task GivenAnInvalidReplyTo_WhenSent_ThenFailsAndSendsNothing(string replyTo)
    {
        var output = await _service.SendEmailAsync(Message() with { ReplyTo = replyTo });

        Assert.False(output.Success);
        Assert.Null(_handler.LastRequest);
        Assert.Contains(output.Errors, e => e.Contains("reply-to"));
    }

    [Fact]
    public async Task GivenAnInvalidSender_WhenSent_ThenTheErrorDoesNotRepeatTheValue()
    {
        var output = await _service.SendEmailAsync(Message() with { From = "secret-value" });

        Assert.DoesNotContain(output.Errors, e => e.Contains("secret-value"));
    }

    [Fact]
    public async Task GivenACanceledToken_WhenSendingAMessage_ThenItThrowsAndSendsNothing()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.SendEmailAsync(Message(), source.Token));

        Assert.Null(_handler.LastRequest);
    }

    [Fact]
    public async Task GivenANullMessage_WhenSent_ThenItThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SendEmailAsync((EmailMessage)null!));
    }

    [Fact]
    public async Task GivenAnImplementationWithoutTheOverload_WhenSentASender_ThenFailsWithoutCallingIt()
    {
        var legacy = new ThreeArgumentEmailService();

        var output = await ((IEmailService)legacy).SendEmailAsync(Message() with { From = "client@example.com" });

        Assert.False(output.Success);
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task GivenAnImplementationWithoutTheOverload_WhenSentAReplyTo_ThenFailsWithoutCallingIt()
    {
        var legacy = new ThreeArgumentEmailService();

        var output = await ((IEmailService)legacy).SendEmailAsync(Message() with { ReplyTo = "visitor@example.com" });

        Assert.False(output.Success);
        Assert.Equal(0, legacy.Calls);
    }

    [Fact]
    public async Task GivenAnImplementationWithoutTheOverload_WhenSentAPlainMessage_ThenDelegatesToTheThreeArgumentMethod()
    {
        var legacy = new ThreeArgumentEmailService();

        var output = await ((IEmailService)legacy).SendEmailAsync(Message());

        Assert.True(output.Success);
        Assert.Equal(1, legacy.Calls);
    }

    /// <summary>An implementation written before messages could carry a sender or a reply-to.</summary>
    private sealed class ThreeArgumentEmailService : IEmailService
    {
        public int Calls { get; private set; }

        public Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
        {
            Calls++;

            return Task.FromResult(new ProcessOutput());
        }
    }
}
