using System.Net;
using System.Net.Sockets;

namespace AdChecker.Core.Guardrails;

public sealed record GuardResult(Uri? Url, string? Error)
{
    public bool IsAllowed => Error is null;

    public static GuardResult Allow(Uri url) => new(url, null);

    public static GuardResult Block(string error) => new(null, error);
}

/// <summary>
/// SSRF protection for image URLs: HTTPS only, default port, no credentials, no IP-address hosts,
/// host must be on the allow-list, and (optionally) every DNS address must be public.
/// Always pass the returned, normalized <see cref="Uri"/> downstream, never the raw string.
/// </summary>
public sealed class ImageUrlGuard
{
    private readonly GuardrailOptions _options;
    private readonly IHostResolver _resolver;
    private readonly HashSet<string> _exactHosts;
    private readonly string[] _wildcardSuffixes;

    public ImageUrlGuard(GuardrailOptions options, IHostResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);
        _options = options;
        _resolver = resolver;

        var entries = options.AllowedImageHosts.Select(NormalizeHost).Where(h => h.Length > 0).ToArray();
        foreach (var entry in entries)
        {
            var name = entry.StartsWith("*.", StringComparison.Ordinal) ? entry[2..] : entry;
            if (IPAddress.TryParse(name, out _) || name.Contains('*', StringComparison.Ordinal) || !name.Contains('.', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid image host allow-list entry '{entry}'. Use a DNS name such as 'images.example.com' or '*.example.com'.", nameof(options));
            }
        }

        _exactHosts = entries.Where(e => !e.StartsWith("*.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        _wildcardSuffixes = entries.Where(e => e.StartsWith("*.", StringComparison.Ordinal)).Select(e => e[1..]).ToArray(); // ".example.com"
    }

    /// <summary>Checks that need no network. Pure and fast.</summary>
    public GuardResult CheckSyntax(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return GuardResult.Block("imageUrl is empty.");
        }

        if (rawUrl.Length > _options.MaxImageUrlLength)
        {
            return GuardResult.Block($"imageUrl is longer than {_options.MaxImageUrlLength} characters.");
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var url))
        {
            return GuardResult.Block("imageUrl must be an absolute URL.");
        }

        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return GuardResult.Block("imageUrl must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            return GuardResult.Block("imageUrl must not contain a user name or password.");
        }

        if (!url.IsDefaultPort)
        {
            return GuardResult.Block("imageUrl must use the default HTTPS port (443).");
        }

        var host = NormalizeHost(url.IdnHost);
        if (url.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || IPAddress.TryParse(host, out _))
        {
            return GuardResult.Block("imageUrl must use a host name, not an IP address.");
        }

        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return GuardResult.Block("imageUrl must not point to localhost.");
        }

        if (!IsAllowListed(host))
        {
            return GuardResult.Block($"Host '{host}' is not on the image host allow-list.");
        }

        return GuardResult.Allow(url);
    }

    /// <summary>Syntax checks, then DNS checks (if enabled).</summary>
    public async Task<GuardResult> CheckAsync(string? rawUrl, CancellationToken cancellationToken = default)
    {
        var syntax = CheckSyntax(rawUrl);
        if (!syntax.IsAllowed || !_options.ResolveImageHosts)
        {
            return syntax;
        }

        var host = NormalizeHost(syntax.Url!.IdnHost);
        IPAddress[] addresses;
        try
        {
            addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return GuardResult.Block($"Host '{host}' could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            return GuardResult.Block($"Host '{host}' could not be resolved.");
        }

        if (addresses.Any(a => !IpAddressRules.IsPublic(a)))
        {
            return GuardResult.Block($"Host '{host}' resolves to a private or reserved address.");
        }

        return syntax;
    }

    private bool IsAllowListed(string host) =>
        _exactHosts.Contains(host) ||
        _wildcardSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal) && host.Length > suffix.Length);

    private static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();
}
