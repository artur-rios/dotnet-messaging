using System.Net;
using System.Text;
using ArturRios.Messaging.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArturRios.Messaging.Tests.Functional;

/// <summary>
/// Sends through a real HTTP server on the loopback interface, resolving the service the way the README
/// documents — <c>AddHttpClient&lt;IEmailService, MailgunEmailService&gt;</c> — so the request really
/// crosses a socket and the registration really has to work.
/// </summary>
[Trait("Category", "Functional")]
[Collection(MailgunEnvironmentCollection.Name)]
public sealed class MailgunOverLoopbackTests : IAsyncLifetime
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<RecordedRequest> _received = [];

    private Task _serverLoop = Task.CompletedTask;
    private ServiceProvider _provider = null!;
    private string _baseAddress = string.Empty;

    private (HttpStatusCode Status, string Body) _response = (HttpStatusCode.OK, """{"message":"Queued."}""");

    public Task InitializeAsync()
    {
        var port = FreeTcpPort();

        _baseAddress = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_baseAddress);
        _listener.Start();

        _serverLoop = Task.Run(ServeAsync);

        var services = new ServiceCollection();

        services.AddSingleton<ILogger<MailgunEmailService>>(NullLogger<MailgunEmailService>.Instance);
        services.AddHttpClient<IEmailService, MailgunEmailService>();

        _provider = services.BuildServiceProvider();

        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, "test-key");
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, "sandbox.example.org");

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiKeyVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.DomainVariable, null);
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, null);

        await _shutdown.CancelAsync();

        _listener.Close();
        await _provider.DisposeAsync();

        try
        {
            await _serverLoop;
        }
        catch (Exception)
        {
            // The listener is torn down under the loop on purpose.
        }

        _shutdown.Dispose();
    }

    /// <summary>
    /// The service composes its own absolute URL against api.mailgun.net, so a loopback test needs a client
    /// pointed at the listener. This mirrors what <c>AddHttpClient</c> hands the service, with the base
    /// address swapped and a handler that rewrites the host.
    /// </summary>
    private IEmailService Service() =>
        new MailgunEmailService(
            NullLogger<MailgunEmailService>.Instance,
            new HttpClient(new RedirectToLoopbackHandler(new Uri(_baseAddress))));

    [Fact]
    public void GivenTheDocumentedRegistration_WhenTheServiceIsResolved_ThenItComesOutOfTheContainer()
    {
        var service = _provider.GetRequiredService<IEmailService>();

        Assert.IsType<MailgunEmailService>(service);
    }

    [Fact]
    public async Task GivenAServerThatAccepts_WhenSendingAnEmail_ThenTheEnvelopeIsSuccessful()
    {
        _response = (HttpStatusCode.OK, """{"id":"<20260824.1@example.org>","message":"Queued. Thank you."}""");

        var output = await Service().SendEmailAsync("to@example.com", "Welcome!", "Thanks for signing up.");

        Assert.True(output.Success);
        Assert.Empty(output.Errors);
    }

    [Fact]
    public async Task GivenAServerThatAccepts_WhenSendingAnEmail_ThenTheRequestCarriesTheDocumentedShape()
    {
        await Service().SendEmailAsync("to@example.com", "Welcome!", "Thanks for signing up.");

        var request = Assert.Single(_received);

        Assert.Equal("POST", request.Method);
        Assert.Equal($"/{MailgunEmailService.DefaultMailgunApiVersion}/sandbox.example.org/messages", request.Path);
        Assert.StartsWith("Basic ", request.Authorization);
        Assert.Equal("api:test-key", DecodeBasic(request.Authorization));

        var decoded = WebUtility.UrlDecode(request.Body);

        Assert.Contains("to=to@example.com", decoded);
        Assert.Contains("subject=Welcome!", decoded);
        Assert.Contains("text=Thanks for signing up.", decoded);
        Assert.Contains("postmaster@sandbox.example.org", decoded);
    }

    [Fact]
    public async Task GivenAnApiVersionOverride_WhenSendingAnEmail_ThenTheUrlUsesIt()
    {
        Environment.SetEnvironmentVariable(MailgunEmailService.ApiVersionVariable, "v4");

        await Service().SendEmailAsync("to@example.com", "Welcome!", "Body");

        Assert.Equal("/v4/sandbox.example.org/messages", Assert.Single(_received).Path);
    }

    [Fact]
    public async Task GivenAServerThatRejects_WhenSendingAnEmail_ThenTheEnvelopeCarriesTheStatusAndBody()
    {
        _response = (HttpStatusCode.Unauthorized, """{"message":"Forbidden"}""");

        var output = await Service().SendEmailAsync("to@example.com", "Welcome!", "Body");

        Assert.False(output.Success);

        var error = Assert.Single(output.Errors);

        Assert.Contains("Unauthorized", error);
        Assert.Contains("Forbidden", error);
    }

    [Fact]
    public async Task GivenTheSameClientUsedTwice_WhenSendingTwoEmails_ThenBothCarryTheirOwnCredential()
    {
        var service = Service();

        await service.SendEmailAsync("first@example.com", "One", "Body");
        await service.SendEmailAsync("second@example.com", "Two", "Body");

        Assert.Equal(2, _received.Count);
        Assert.All(_received, request => Assert.Equal("api:test-key", DecodeBasic(request.Authorization)));
    }

    private static string DecodeBasic(string authorization) =>
        Encoding.ASCII.GetString(Convert.FromBase64String(authorization["Basic ".Length..]));

    private async Task ServeAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);

            var recorded = new RecordedRequest(
                context.Request.HttpMethod,
                context.Request.Url?.AbsolutePath ?? string.Empty,
                context.Request.Headers["Authorization"] ?? string.Empty,
                await reader.ReadToEndAsync());

            lock (_received)
            {
                _received.Add(recorded);
            }

            var buffer = Encoding.UTF8.GetBytes(_response.Body);

            context.Response.StatusCode = (int)_response.Status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;

            await context.Response.OutputStream.WriteAsync(buffer);

            context.Response.Close();
        }
    }

    private static int FreeTcpPort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        socket.Start();

        var port = ((IPEndPoint)socket.LocalEndpoint).Port;

        socket.Stop();

        return port;
    }

    private sealed record RecordedRequest(string Method, string Path, string Authorization, string Body);

    /// <summary>
    /// Rewrites the scheme, host and port of every outgoing request to the loopback listener, leaving the
    /// path the service composed untouched.
    /// </summary>
    private sealed class RedirectToLoopbackHandler(Uri target) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.RequestUri = new UriBuilder(request.RequestUri!)
            {
                Scheme = target.Scheme,
                Host = target.Host,
                Port = target.Port
            }.Uri;

            return base.SendAsync(request, cancellationToken);
        }
    }
}
