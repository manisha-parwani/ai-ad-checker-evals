namespace AdChecker.Core.Guardrails;

/// <summary>Input limits. Bound from the "AdChecker:Guardrails" configuration section.</summary>
public sealed class GuardrailOptions
{
    public const int DefaultMaxTextLength = 2000;

    /// <summary>Maximum ad text length in UTF-16 characters.</summary>
    public int MaxTextLength { get; set; } = DefaultMaxTextLength;

    public int MaxImageUrlLength { get; set; } = 2048;

    /// <summary>
    /// Hosts that may serve ad images. Exact names ("images.example.com") or one wildcard level
    /// ("*.cdn.example.com", which matches subdomains but not "cdn.example.com" itself).
    /// Empty list = no image URLs are accepted.
    /// </summary>
    public IList<string> AllowedImageHosts { get; set; } = [];

    /// <summary>
    /// Resolve the image host with DNS and block it if any address is private or reserved.
    /// Defense in depth against DNS records that point at internal addresses.
    /// </summary>
    public bool ResolveImageHosts { get; set; } = true;
}
