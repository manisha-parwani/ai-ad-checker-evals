using System.Net;
using System.Text;

namespace AdChecker.Tests.TestDoubles;

/// <summary>Returns queued responses in order and records every request (with its body).</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpMethod Method, Uri Uri, string? Authorization, string? Body)> Requests { get; } = [];

    /// <summary>Used when the queue is empty.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Fallback { get; set; }

    public StubHttpHandler Enqueue(HttpStatusCode status, string? json = null, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        if (_responses.TryDequeue(out var next))
        {
            return next(request);
        }

        return Fallback?.Invoke(request) ?? throw new InvalidOperationException($"No stub response left for {request.Method} {request.RequestUri}");
    }
}
