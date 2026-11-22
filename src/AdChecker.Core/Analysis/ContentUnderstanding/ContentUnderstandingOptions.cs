using System.Text.RegularExpressions;

namespace AdChecker.Core.Analysis.ContentUnderstanding;

/// <summary>Bound from the "ContentUnderstanding" configuration section (env vars: ContentUnderstanding__Endpoint, ...).</summary>
public sealed partial class ContentUnderstandingOptions
{
    /// <summary>GA REST API version of Azure AI Content Understanding used by this sample.</summary>
    public const string DefaultApiVersion = "2025-11-01";

    /// <summary>Resource endpoint, for example https://my-resource.services.ai.azure.com/. Not a secret, but keep it out of git.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Id of the custom analyzer created from analyzer/brand-safety-analyzer.json.</summary>
    public string AnalyzerId { get; set; } = "brandSafety";

    public string ApiVersion { get; set; } = DefaultApiVersion;

    /// <summary>Wait between status polls when the service sends no Retry-After header.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Total time allowed for one analysis, including polling.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "ContentUnderstanding:Endpoint must be an absolute HTTPS URL. Set the ContentUnderstanding__Endpoint environment variable.");
        }

        if (!AnalyzerIdPattern().IsMatch(AnalyzerId ?? string.Empty))
        {
            throw new InvalidOperationException("ContentUnderstanding:AnalyzerId must match ^[a-zA-Z0-9._-]{1,64}$.");
        }

        if (string.IsNullOrWhiteSpace(ApiVersion))
        {
            throw new InvalidOperationException("ContentUnderstanding:ApiVersion is required.");
        }

        if (PollInterval < TimeSpan.Zero || Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("ContentUnderstanding:PollInterval must be >= 0 and Timeout must be > 0.");
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]{1,64}$")]
    private static partial Regex AnalyzerIdPattern();
}
