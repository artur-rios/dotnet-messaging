using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// The sender is configurable through <c>MAILGUN_FROM</c>; without it, mail goes out as the domain's postmaster
/// with no display name, never as the "Mailgun Sandbox" the service used to hard-code.
/// </summary>
[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunSenderTests : IDisposable
{
    private const string Domain = "mg.example.com";

    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunSenderTests()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, Domain);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, null);

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

    private string? SentFrom()
    {
        foreach (var pair in _handler.LastRequestBody!.Split('&'))
        {
            var parts = pair.Split('=', 2);

            if (WebUtility.UrlDecode(parts[0]) == "from")
            {
                return WebUtility.UrlDecode(parts[1]);
            }
        }

        return null;
    }

    [Fact]
    public void GivenTheSenderVariable_WhenReadingItsName_ThenItIsMailgunFrom()
    {
        Assert.Equal("MAILGUN_FROM", MailgunEmailService.FromVariable);
    }

    [Fact]
    public async Task GivenNoConfiguredSender_WhenSendingAnEmail_ThenItIsSentAsThePostmasterWithoutADisplayName()
    {
        var output = await _service.SendEmailAsync("to@example.com", "Subject", "Body");

        Assert.True(output.Success);
        Assert.Equal($"postmaster@{Domain}", SentFrom());
        Assert.DoesNotContain("Sandbox", _handler.LastRequestBody!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankSender_WhenSendingAnEmail_ThenThePostmasterDefaultIsUsed(string from)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, from);

        var output = await _service.SendEmailAsync("to@example.com", "Subject", "Body");

        Assert.True(output.Success);
        Assert.Equal($"postmaster@{Domain}", SentFrom());
    }

    [Theory]
    [InlineData("no-reply@acme.com")]
    [InlineData("Acme <no-reply@acme.com>")]
    [InlineData("\"Acme, Inc.\" <no-reply@acme.com>")]
    [InlineData("Zoë at Acme <zoe@acme.com>")]
    public async Task GivenAConfiguredSender_WhenSendingAnEmail_ThenItIsSentFromThatSender(string from)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, from);

        var output = await _service.SendEmailAsync("to@example.com", "Subject", "Body");

        Assert.True(output.Success);
        Assert.Equal(from, SentFrom());
    }

    [Fact]
    public async Task GivenAConfiguredSenderWithSurroundingWhitespace_WhenSendingAnEmail_ThenItIsTrimmed()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, "  Acme <no-reply@acme.com>  ");

        await _service.SendEmailAsync("to@example.com", "Subject", "Body");

        Assert.Equal("Acme <no-reply@acme.com>", SentFrom());
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("Acme")]
    [InlineData("Acme <not-an-email>")]
    [InlineData("no-reply@acme.com, other@acme.com")]
    [InlineData("no-reply@acme.com, Acme <other@acme.com>")]
    [InlineData("<no-reply@acme.com>, <other@acme.com>")]
    [InlineData("no-reply@acme.com\r\nBcc: victim@example.com")]
    public async Task GivenAnInvalidSender_WhenSendingAnEmail_ThenTheEnvelopeSaysSoAndNothingIsSent(string from)
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.FromVariable, from);

        var output = await _service.SendEmailAsync("to@example.com", "Subject", "Body");

        Assert.False(output.Success);
        Assert.Contains(output.Errors, error => error.Contains(MailgunEmailService.FromVariable));
        Assert.Null(_handler.LastRequest);
    }
}
