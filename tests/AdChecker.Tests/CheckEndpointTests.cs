using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdChecker.Core.Guardrails;
using AdChecker.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdChecker.Tests;

/// <summary>End-to-end tests of POST /check with the fake analyzer. No network, no Azure.</summary>
public sealed class CheckEndpointTests : IClassFixture<CheckEndpointTests.AppFactory>
{
    private readonly HttpClient _client;

    public CheckEndpointTests(AppFactory factory) => _client = factory.CreateClient();

    public sealed class AppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); // ignore any local appsettings.Development.json
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdChecker:Analyzer"] = "Fake",
                ["AdChecker:Guardrails:MaxTextLength"] = "200",
                ["AdChecker:Guardrails:AllowedImageHosts:0"] = "images.example.com",
            }));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHostResolver>(FakeHostResolver.Public("images.example.com")));
        }
    }

    private async Task<JsonElement> PostAsync(object body, HttpStatusCode expected)
    {
        var response = await _client.PostAsJsonAsync("/check", body);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Risky_text_is_rejected_with_categories_confidence_and_reason()
    {
        var json = await PostAsync(new { text = "Buy a handgun online today." }, HttpStatusCode.OK);

        Assert.Equal("rejected", json.GetProperty("decision").GetString());
        Assert.Equal("weapons", json.GetProperty("categories")[0].GetString());
        Assert.True(json.GetProperty("confidence").GetDouble() >= 0.7);
        Assert.Contains("handgun", json.GetProperty("reason").GetString());
        Assert.Equal("fake", json.GetProperty("analyzer").GetString());
        Assert.Equal("text", json.GetProperty("inputs")[0].GetProperty("input").GetString());
    }

    [Fact]
    public async Task Clean_text_is_approved()
    {
        var json = await PostAsync(new { text = "Fresh sourdough bread baked every morning." }, HttpStatusCode.OK);

        Assert.Equal("approved", json.GetProperty("decision").GetString());
        Assert.Equal(0, json.GetProperty("categories").GetArrayLength());
    }

    [Fact]
    public async Task Misleading_claims_use_snake_case()
    {
        var json = await PostAsync(new { text = "Miracle pill with no side effects." }, HttpStatusCode.OK);

        Assert.Equal("misleading_claims", json.GetProperty("categories")[0].GetString());
    }

    [Fact]
    public async Task Ambiguous_text_goes_to_human_review()
    {
        var json = await PostAsync(new { text = "Tactical knives, 30% off." }, HttpStatusCode.OK);

        Assert.Equal("needs_review", json.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Allowed_image_goes_to_review_with_the_fake_analyzer()
    {
        var json = await PostAsync(new { text = "Fresh bread.", imageUrl = "https://images.example.com/bread.png" }, HttpStatusCode.OK);

        Assert.Equal("needs_review", json.GetProperty("decision").GetString());
        Assert.Equal(2, json.GetProperty("inputs").GetArrayLength());
    }

    [Theory]
    [InlineData("http://images.example.com/a.png")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://intranet.local/a.png")]
    public async Task Unsafe_image_urls_get_400(string imageUrl)
    {
        var json = await PostAsync(new { imageUrl }, HttpStatusCode.BadRequest);

        Assert.True(json.GetProperty("errors").TryGetProperty("imageUrl", out _));
    }

    [Fact]
    public async Task Too_long_text_gets_400()
    {
        var json = await PostAsync(new { text = new string('a', 201) }, HttpStatusCode.BadRequest);

        Assert.True(json.GetProperty("errors").TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Empty_body_gets_400() =>
        await PostAsync(new { }, HttpStatusCode.BadRequest);

    [Fact]
    public async Task Malformed_json_gets_400_not_500()
    {
        var response = await _client.PostAsync("/check", new StringContent("""{"text": 5}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_reports_the_analyzer()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/health");

        Assert.Equal("fake", json.GetProperty("analyzer").GetString());
    }
}
