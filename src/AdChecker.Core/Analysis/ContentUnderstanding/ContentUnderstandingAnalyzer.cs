using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AdChecker.Core.Model;
using Azure.Core;
using Azure.Identity;

namespace AdChecker.Core.Analysis.ContentUnderstanding;

/// <summary>
/// Calls the Azure AI Content Understanding REST API (GA version 2025-11-01):
/// <c>POST {endpoint}/contentunderstanding/analyzers/{analyzerId}:analyze</c>, then polls the
/// <c>Operation-Location</c> URL until the long-running operation finishes.
/// Authentication uses a Microsoft Entra ID token (no API keys).
/// </summary>
public sealed class ContentUnderstandingAnalyzer : IContentAnalyzer
{
    /// <summary>Entra ID scope for Azure AI services.</summary>
    public const string TokenScope = "https://cognitiveservices.azure.com/.default";

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly ContentUnderstandingOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private AccessToken _token;

    public ContentUnderstandingAnalyzer(
        HttpClient http,
        TokenCredential credential,
        ContentUnderstandingOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _http = http;
        _credential = credential;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Name => "content-understanding";

    /// <summary>An HttpClient that is safe to keep for the whole process (connections are recycled for DNS changes).</summary>
    public static HttpClient CreateHttpClient() =>
        new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            Timeout = Timeout.InfiniteTimeSpan, // the per-analysis deadline below is the real timeout
        };

    public async Task<AnalyzerResult> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var deadline = new CancellationTokenSource(_options.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var operationUrl = await StartAnalysisAsync(input, linked.Token).ConfigureAwait(false);
            using var operation = await WaitForResultAsync(operationUrl, linked.Token).ConfigureAwait(false);
            return ContentUnderstandingResultParser.Parse(operation.RootElement);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AnalyzerException($"Content Understanding did not finish within {_options.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AnalyzerException("Could not reach Content Understanding.", ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new AnalyzerException("Could not get a Microsoft Entra ID token. Run 'az login' locally, or check the managed identity.", ex);
        }
        catch (JsonException ex)
        {
            throw new AnalyzerException("Content Understanding returned JSON this client does not understand.", ex);
        }
    }

    private async Task<Uri> StartAnalysisAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        var url = new Uri(
            $"{_options.Endpoint!.AbsoluteUri.TrimEnd('/')}/contentunderstanding/analyzers/{_options.AnalyzerId}:analyze" +
            $"?api-version={Uri.EscapeDataString(_options.ApiVersion)}");

        var body = new Dictionary<string, object> { ["inputs"] = new[] { ToRequestInput(input) } };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        await AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ServiceErrorAsync(response, "start the analysis", cancellationToken).ConfigureAwait(false);
        }

        var location = response.Headers.TryGetValues("Operation-Location", out var values) ? values.FirstOrDefault() : null;
        if (!Uri.TryCreate(location, UriKind.Absolute, out var operationUrl))
        {
            throw new AnalyzerException("Content Understanding did not return an Operation-Location header.");
        }

        // Never send our bearer token to a host other than the configured endpoint.
        if (operationUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(operationUrl.IdnHost, _options.Endpoint.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new AnalyzerException("Operation-Location points to an unexpected host. Refusing to poll it.");
        }

        return operationUrl;
    }

    private async Task<JsonDocument> WaitForResultAsync(Uri operationUrl, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, operationUrl);
            await AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                // Transient: wait and poll again. The overall deadline still applies.
                await Task.Delay(RetryDelay(response), _time, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ServiceErrorAsync(response, "read the analysis status", cancellationToken).ConfigureAwait(false);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var status = document.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;

            if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }

            using (document)
            {
                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AnalyzerException($"Content Understanding analysis {status!.ToLowerInvariant()}: {ErrorMessage(document.RootElement)}");
                }
            }

            // NotStarted or Running.
            await Task.Delay(RetryDelay(response), _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> ToRequestInput(AnalysisInput input) => input.Kind switch
    {
        InputKind.Text => new()
        {
            ["name"] = "ad-text",
            ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(input.Text!)),
            ["mimeType"] = "text/plain",
        },
        InputKind.Image => new()
        {
            ["name"] = "ad-image",
            ["url"] = input.ImageUrl!.AbsoluteUri, // normalized URL that passed the guardrails
        },
        _ => throw new ArgumentOutOfRangeException(nameof(input), input.Kind, "Unknown input kind."),
    };

    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token.Token is null || _token.ExpiresOn - TokenRefreshMargin <= _time.GetUtcNow())
            {
                _token = await _credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken).ConfigureAwait(false);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Token);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? delay = retryAfter?.Delta ?? (retryAfter?.Date - _time.GetUtcNow());
        if (delay is not { } value || value < TimeSpan.Zero)
        {
            return _options.PollInterval;
        }

        return value > MaxRetryAfter ? MaxRetryAfter : value;
    }

    private static async Task<AnalyzerException> ServiceErrorAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        var detail = string.Empty;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            detail = ErrorMessage(document.RootElement);
        }
        catch (JsonException)
        {
            // Body is not JSON; the status code is enough.
        }

        var hint = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => " Check that you are signed in to the right tenant.",
            HttpStatusCode.Forbidden => " Your identity needs the 'Cognitive Services User' role on the resource.",
            HttpStatusCode.NotFound => " Check the endpoint and that the analyzer exists (see scripts/create-analyzer.sh).",
            _ => string.Empty,
        };

        return new AnalyzerException($"Could not {action}: HTTP {(int)response.StatusCode}. {detail}{hint}".Trim());
    }

    private static string ErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? $"{Get(error, "code")} {Get(error, "message")}".Trim()
            : string.Empty;

    private static string? Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
