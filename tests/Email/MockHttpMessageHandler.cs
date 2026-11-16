using System.Net;
using System.Text;

namespace ArturRios.Messaging.Tests.Email;

/// <summary>
/// Stands in for Mailgun: answers every request with the status and body last set, and records each request it
/// receives, its body included.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string _responseContent = "{}";

    public HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>
    /// The request body, buffered while the request is still alive. The service disposes the request it
    /// builds, so a test cannot read the content afterwards.
    /// </summary>
    public string? LastRequestBody { get; private set; }

    /// <summary>Every request received, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// Runs on every request after it is recorded and before the response is built: to hold the request, or to
    /// throw what the transport would.
    /// </summary>
    public Func<CancellationToken, Task> OnRequest { get; set; } = _ => Task.CompletedTask;

    public void SetResponse(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _responseContent = content;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (_requests)
        {
            LastRequest = request;
            LastRequestBody = body;
            _requests.Add(new RecordedRequest(request.RequestUri, request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter, body));
        }

        await OnRequest(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>What a request carried, kept after the service disposes it.</summary>
    internal sealed record RecordedRequest(
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}
