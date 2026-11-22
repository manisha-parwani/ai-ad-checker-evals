using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdChecker.Core.Analysis;
using AdChecker.Core.Analysis.ContentUnderstanding;
using AdChecker.Core.Model;
using AdChecker.Tests.TestDoubles;

namespace AdChecker.Tests;

/// <summary>
/// Offline tests for the REST client. Response shapes follow the public 2025-11-01 API reference;
/// they are hand-written fixtures, not recordings from a live service.
/// </summary>
public class ContentUnderstandingAnalyzerTests
{
    private const string Endpoint = "https://my-cu.services.ai.azure.com/";
    private const string OperationUrl = "https://my-cu.services.ai.azure.com/contentunderstanding/analyzerResults/op-1?api-version=2025-11-01";

    private readonly StubHttpHandler _handler = new();
    private readonly FakeTokenCredential _credential = new();

    private ContentUnderstandingAnalyzer CreateAnalyzer(TimeSpan? timeout = null) => new(
        new HttpClient(_handler),
        _credential,
        new ContentUnderstandingOptions
        {
            Endpoint = new Uri(Endpoint),
            AnalyzerId = "brandSafety",
            PollInterval = TimeSpan.FromMilliseconds(5),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        });

    internal static string SucceededJson(string primary, double? score, string reason, params string[] others)
    {
        var fields = new Dictionary<string, object>
        {
            ["PrimaryCategory"] = new { type = "string", valueString = primary },
            ["OtherCategories"] = new { type = "array", valueArray = others.Select(o => new { type = "string", valueString = o }).ToArray() },
            ["Reason"] = new { type = "string", valueString = reason },
        };
        if (score is not null)
        {
            fields["ConfidenceScore"] = new { type = "number", valueNumber = score };
        }

        return JsonSerializer.Serialize(new
        {
            id = "op-1",
            status = "Succeeded",
            result = new
            {
                analyzerId = "brandSafety",
                apiVersion = "2025-11-01",
                contents = new[] { new { kind = "document", mimeType = "text/plain", markdown = "ad", fields } },
            },
        });
    }

    private static void WithOperationLocation(HttpResponseMessage response, string url = OperationUrl) =>
        response.Headers.Add("Operation-Location", url);

    [Fact]
    public async Task Text_is_sent_as_base64_plain_text_with_a_bearer_token_and_the_result_is_polled()
    {
        _handler
            .Enqueue(HttpStatusCode.Accepted, """{"id":"op-1","status":"NotStarted"}""", r => WithOperationLocation(r))
            .Enqueue(HttpStatusCode.OK, """{"id":"op-1","status":"Running"}""")
            .Enqueue(HttpStatusCode.OK, SucceededJson("gambling", 0.92, "Promotes an online casino.", "misleading_claims"));

        var result = await CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("Free spins at our casino"));

        Assert.Equal([RiskCategory.Gambling, RiskCategory.MisleadingClaims], result.Categories);
        Assert.Equal(0.92, result.Confidence);
        Assert.Equal("Promotes an online casino.", result.Reason);

        Assert.Equal(3, _handler.Requests.Count);
        var start = _handler.Requests[0];
        Assert.Equal(HttpMethod.Post, start.Method);
        Assert.Equal("https://my-cu.services.ai.azure.com/contentunderstanding/analyzers/brandSafety:analyze?api-version=2025-11-01", start.Uri.AbsoluteUri);
        Assert.Equal("Bearer test-token", start.Authorization);

        using var body = JsonDocument.Parse(start.Body!);
        var input = body.RootElement.GetProperty("inputs")[0];
        Assert.Equal("text/plain", input.GetProperty("mimeType").GetString());
        Assert.Equal("Free spins at our casino", Encoding.UTF8.GetString(Convert.FromBase64String(input.GetProperty("data").GetString()!)));

        Assert.All(_handler.Requests.Skip(1), r => Assert.Equal(OperationUrl, r.Uri.AbsoluteUri));
        Assert.All(_handler.Requests, r => Assert.Equal("Bearer test-token", r.Authorization));
        Assert.Equal(1, _credential.Calls); // token is cached between calls
        Assert.Equal([ContentUnderstandingAnalyzer.TokenScope], _credential.RequestedScopes[0]);
    }

    [Fact]
    public async Task Image_is_sent_as_a_url()
    {
        _handler
            .Enqueue(HttpStatusCode.Accepted, "{}", r => WithOperationLocation(r))
            .Enqueue(HttpStatusCode.OK, SucceededJson("none", 0.9, "A bicycle on a road."));

        var result = await CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForImage(new Uri("https://images.example.com/bike.png")));

        Assert.Empty(result.Categories);
        using var body = JsonDocument.Parse(_handler.Requests[0].Body!);
        Assert.Equal("https://images.example.com/bike.png", body.RootElement.GetProperty("inputs")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Failed_operation_throws_with_the_service_message()
    {
        _handler
            .Enqueue(HttpStatusCode.Accepted, "{}", r => WithOperationLocation(r))
            .Enqueue(HttpStatusCode.OK, """{"status":"Failed","error":{"code":"InvalidContent","message":"Could not download the file."}}""");

        var ex = await Assert.ThrowsAsync<AnalyzerException>(() => CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("hi")));

        Assert.Contains("failed", ex.Message);
        Assert.Contains("Could not download the file.", ex.Message);
    }

    [Fact]
    public async Task Operation_location_on_another_host_is_never_polled()
    {
        _handler.Enqueue(HttpStatusCode.Accepted, "{}", r => WithOperationLocation(r, "https://attacker.example.org/steal?api-version=2025-11-01"));

        var ex = await Assert.ThrowsAsync<AnalyzerException>(() => CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("hi")));

        Assert.Contains("unexpected host", ex.Message);
        Assert.Single(_handler.Requests); // the token was not sent anywhere else
    }

    [Fact]
    public async Task Missing_operation_location_throws()
    {
        _handler.Enqueue(HttpStatusCode.Accepted, "{}");

        var ex = await Assert.ThrowsAsync<AnalyzerException>(() => CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("hi")));

        Assert.Contains("Operation-Location", ex.Message);
    }

    [Fact]
    public async Task Forbidden_explains_the_missing_role()
    {
        _handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":"PermissionDenied","message":"Principal does not have access."}}""");

        var ex = await Assert.ThrowsAsync<AnalyzerException>(() => CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("hi")));

        Assert.Contains("HTTP 403", ex.Message);
        Assert.Contains("Cognitive Services User", ex.Message);
    }

    [Fact]
    public async Task Throttling_while_polling_is_retried()
    {
        _handler
            .Enqueue(HttpStatusCode.Accepted, "{}", r => WithOperationLocation(r))
            .Enqueue(HttpStatusCode.TooManyRequests, null, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero))
            .Enqueue(HttpStatusCode.ServiceUnavailable)
            .Enqueue(HttpStatusCode.OK, SucceededJson("alcohol", 0.8, "Beer offer."));

        var result = await CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("Two beers for 5 euros"));

        Assert.Equal([RiskCategory.Alcohol], result.Categories);
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_timeout()
    {
        _handler.Enqueue(HttpStatusCode.Accepted, "{}", r => WithOperationLocation(r));
        _handler.Fallback = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"Running"}""", Encoding.UTF8, "application/json"),
        };

        var ex = await Assert.ThrowsAsync<AnalyzerException>(() =>
            CreateAnalyzer(timeout: TimeSpan.FromMilliseconds(200)).AnalyzeAsync(AnalysisInput.ForText("hi")));

        Assert.Contains("did not finish", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateAnalyzer().AnalyzeAsync(AnalysisInput.ForText("hi"), cts.Token));
    }

    [Theory]
    [InlineData("http://my-cu.services.ai.azure.com/", "brandSafety")] // not HTTPS
    [InlineData("https://my-cu.services.ai.azure.com/", "bad/id")]
    [InlineData("https://my-cu.services.ai.azure.com/", "")]
    public void Invalid_options_fail_fast(string endpoint, string analyzerId) =>
        Assert.Throws<InvalidOperationException>(() => new ContentUnderstandingAnalyzer(
            new HttpClient(_handler),
            _credential,
            new ContentUnderstandingOptions { Endpoint = new Uri(endpoint), AnalyzerId = analyzerId }));

    [Fact]
    public void Missing_endpoint_fails_fast() =>
        Assert.Throws<InvalidOperationException>(() => new ContentUnderstandingAnalyzer(new HttpClient(_handler), _credential, new ContentUnderstandingOptions()));
}
