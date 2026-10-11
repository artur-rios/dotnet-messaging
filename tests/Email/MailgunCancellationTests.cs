using System.Net;
using System.Text;
using ArturRios.Messaging.Email;
using ArturRios.Output;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// Every asynchronous entry point takes a <see cref="CancellationToken"/>, as in the rest of the family: an
/// already-canceled token throws before anything is sent, and canceling mid-send aborts the HTTP request.
/// </summary>
[Trait("Category", "Unit")]
[Collection(MailgunEnvironmentCollection.Name)]
public class MailgunCancellationTests : IDisposable
{
    private readonly BlockingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly MailgunEmailService _service;

    public MailgunCancellationTests()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "mg.example.com");

        _httpClient = new HttpClient(_handler);
        _service = new MailgunEmailService(NullLogger<MailgunEmailService>.Instance, _httpClient);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GivenACanceledToken_WhenSendingAnEmail_ThenCancellationIsThrownAndNothingIsSent()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.SendEmailAsync("to@example.com", "s", "b", cancellation.Token));

        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task GivenASendInFlight_WhenTheTokenIsCanceled_ThenTheRequestIsAborted()
    {
        using var cancellation = new CancellationTokenSource();

        var send = _service.SendEmailAsync("to@example.com", "s", "b", cancellation.Token);
        await _handler.RequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task GivenTheInterface_WhenSendingWithAToken_ThenMailgunReceivesIt()
    {
        IEmailService service = _service;
        using var cancellation = new CancellationTokenSource();

        var send = service.SendEmailAsync("to@example.com", "s", "b", cancellation.Token);
        await _handler.RequestArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task GivenAnImplementationWithoutTheTokenOverload_WhenSendingWithALiveToken_ThenItDelegates()
    {
        var legacy = new LegacyEmailService();
        IEmailService service = legacy;

        var output = await service.SendEmailAsync("to@example.com", "s", "b", CancellationToken.None);

        Assert.True(output.Success);
        Assert.Equal(1, legacy.Calls);
    }

    [Fact]
    public async Task GivenAnImplementationWithoutTheTokenOverload_WhenSendingWithACanceledToken_ThenNothingIsSent()
    {
        var legacy = new LegacyEmailService();
        IEmailService service = legacy;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendEmailAsync("to@example.com", "s", "b", cancellation.Token));

        Assert.Equal(0, legacy.Calls);
    }

    /// <summary>An implementation written against the three-argument contract only.</summary>
    private sealed class LegacyEmailService : IEmailService
    {
        public int Calls { get; private set; }

        public Task<ProcessOutput> SendEmailAsync(string to, string subject, string body)
        {
            Calls++;

            return Task.FromResult(new ProcessOutput());
        }
    }

    /// <summary>Holds every request open until it is canceled.</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        public TaskCompletionSource RequestArrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            RequestArrived.TrySetResult();

            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
