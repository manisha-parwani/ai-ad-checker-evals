using AdChecker.Core.Guardrails;
using AdChecker.Tests.TestDoubles;

namespace AdChecker.Tests;

public class ImageUrlGuardTests
{
    private static ImageUrlGuard CreateGuard(FakeHostResolver? resolver = null, bool resolve = true, params string[] allowList) =>
        new(
            new GuardrailOptions
            {
                AllowedImageHosts = allowList.Length > 0 ? allowList : ["images.example.com", "*.cdn.example.net"],
                ResolveImageHosts = resolve,
            },
            resolver ?? FakeHostResolver.Public("images.example.com", "eu.cdn.example.net"));

    [Theory]
    [InlineData("https://images.example.com/ads/banner.png")]
    [InlineData("https://IMAGES.Example.COM/ads/banner.png")] // host is case-insensitive
    [InlineData("https://images.example.com./ads/banner.png")] // trailing dot
    [InlineData("https://eu.cdn.example.net/a.jpg")] // wildcard entry
    public async Task Allow_listed_https_urls_pass(string url)
    {
        var result = await CreateGuard().CheckAsync(url);

        Assert.True(result.IsAllowed, result.Error);
        Assert.NotNull(result.Url);
    }

    [Theory]
    [InlineData("http://images.example.com/a.png", "HTTPS")]
    [InlineData("ftp://images.example.com/a.png", "HTTPS")]
    [InlineData("file:///etc/passwd", "HTTPS")]
    [InlineData("/relative/a.png", null)] // "absolute URL" on Windows, file:// (so "HTTPS") on Linux and macOS
    [InlineData("not a url", "absolute")]
    [InlineData("", "empty")]
    [InlineData("https://user:secret@images.example.com/a.png", "user name")]
    [InlineData("https://images.example.com:8443/a.png", "port")]
    [InlineData("https://127.0.0.1/a.png", "IP address")]
    [InlineData("https://169.254.169.254/latest/meta-data", "IP address")]
    [InlineData("https://[::1]/a.png", "IP address")]
    [InlineData("https://[::ffff:127.0.0.1]/a.png", "IP address")]
    [InlineData("https://2130706433/a.png", "IP address")] // 127.0.0.1 written as one number
    [InlineData("https://0x7f.0.0.1/a.png", "IP address")] // hex form
    [InlineData("https://localhost/a.png", "localhost")]
    [InlineData("https://evil.example.org/a.png", "allow-list")]
    [InlineData("https://images.example.com.evil.org/a.png", "allow-list")] // suffix trick
    [InlineData("https://evilimages.example.com/a.png", "allow-list")]
    [InlineData("https://cdn.example.net/a.jpg", "allow-list")] // wildcard does not match the apex
    [InlineData("https://xcdn.example.net/a.jpg", "allow-list")] // wildcard needs a dot boundary
    public async Task Unsafe_urls_are_blocked(string url, string? expectedReason)
    {
        var result = await CreateGuard().CheckAsync(url);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Url);
        if (expectedReason is not null)
        {
            Assert.Contains(expectedReason, result.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Very_long_urls_are_blocked()
    {
        var url = "https://images.example.com/" + new string('a', 3000);

        var result = await CreateGuard().CheckAsync(url);

        Assert.Contains("longer than", result.Error);
    }

    [Fact]
    public async Task Allow_listed_host_that_resolves_to_a_private_address_is_blocked()
    {
        // DNS rebinding style: an allowed name that points inside the network.
        var resolver = new FakeHostResolver(new() { ["images.example.com"] = ["93.184.215.14", "10.0.0.5"] });

        var result = await CreateGuard(resolver).CheckAsync("https://images.example.com/a.png");

        Assert.False(result.IsAllowed);
        Assert.Contains("private or reserved", result.Error);
    }

    [Fact]
    public async Task Host_that_does_not_resolve_is_blocked()
    {
        var result = await CreateGuard(new FakeHostResolver([])).CheckAsync("https://images.example.com/a.png");

        Assert.Contains("could not be resolved", result.Error);
    }

    [Fact]
    public async Task Dns_check_can_be_turned_off()
    {
        var result = await CreateGuard(new FakeHostResolver([]), resolve: false).CheckAsync("https://images.example.com/a.png");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Empty_allow_list_blocks_every_image()
    {
        var guard = new ImageUrlGuard(new GuardrailOptions(), FakeHostResolver.Public("images.example.com"));

        var result = await guard.CheckAsync("https://images.example.com/a.png");

        Assert.Contains("allow-list", result.Error);
    }

    [Theory]
    [InlineData(@"https://images.example.com\@evil.org/a.png")] // parser confusion between host and user info
    [InlineData("https://images.example.com@evil.org/a.png")] // "images.example.com" is only the user name here
    public void Host_confusion_tricks_are_blocked(string url) =>
        Assert.False(CreateGuard().CheckSyntax(url).IsAllowed);

    [Fact]
    public void Returned_url_is_the_normalized_one_that_was_checked()
    {
        var result = CreateGuard().CheckSyntax("https://IMAGES.example.com/ads/../banner.png");

        Assert.True(result.IsAllowed, result.Error);
        Assert.Equal("https://images.example.com/banner.png", result.Url!.AbsoluteUri);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("*.*.example.com")]
    [InlineData("localhost")]
    public void Invalid_allow_list_entries_fail_at_startup(string entry) =>
        Assert.Throws<ArgumentException>(() =>
            new ImageUrlGuard(new GuardrailOptions { AllowedImageHosts = [entry] }, new FakeHostResolver([])));
}
