using AdChecker.Core.Guardrails;
using AdChecker.Tests.TestDoubles;

namespace AdChecker.Tests;

public class CheckRequestValidatorTests
{
    private static CheckRequestValidator CreateValidator(int maxTextLength = 50)
    {
        var options = new GuardrailOptions { MaxTextLength = maxTextLength, AllowedImageHosts = ["images.example.com"] };
        return new CheckRequestValidator(options, new ImageUrlGuard(options, FakeHostResolver.Public("images.example.com")));
    }

    [Fact]
    public async Task Text_only_is_valid_and_trimmed()
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest("  Fresh bread daily.  ", null));

        Assert.True(outcome.IsValid);
        Assert.Equal("Fresh bread daily.", outcome.Request!.Text);
        Assert.Null(outcome.Request.ImageUrl);
    }

    [Fact]
    public async Task Image_only_is_valid()
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest(null, "https://images.example.com/a.png"));

        Assert.True(outcome.IsValid);
        Assert.Equal(new Uri("https://images.example.com/a.png"), outcome.Request!.ImageUrl);
    }

    [Fact]
    public async Task Text_and_image_together_are_valid()
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest("Fresh bread.", "https://images.example.com/a.png"));

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task Empty_request_is_rejected()
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest(null, null));

        Assert.False(outcome.IsValid);
        Assert.Contains("request", outcome.Errors.Keys);
    }

    [Fact]
    public async Task Missing_body_is_rejected()
    {
        var outcome = await CreateValidator().ValidateAsync(null);

        Assert.False(outcome.IsValid);
        Assert.Contains("request", outcome.Errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task Blank_text_is_rejected(string text)
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest(text, null));

        Assert.False(outcome.IsValid);
        Assert.Contains("text", outcome.Errors.Keys);
    }

    [Fact]
    public async Task Text_at_the_limit_is_valid_and_one_more_character_is_not()
    {
        var validator = CreateValidator(maxTextLength: 50);

        Assert.True((await validator.ValidateAsync(new CheckRequest(new string('a', 50), null))).IsValid);

        var tooLong = await validator.ValidateAsync(new CheckRequest(new string('a', 51), null));
        Assert.False(tooLong.IsValid);
        Assert.Contains("longer than 50", tooLong.Errors["text"][0]);
    }

    [Fact]
    public async Task Control_characters_are_rejected_but_line_breaks_are_fine()
    {
        var validator = CreateValidator();

        Assert.True((await validator.ValidateAsync(new CheckRequest("Line one\nLine two\r\n\tdone", null))).IsValid);
        Assert.False((await validator.ValidateAsync(new CheckRequest("Hidden\u0000text", null))).IsValid);
    }

    [Fact]
    public async Task Errors_for_text_and_image_are_reported_together()
    {
        var outcome = await CreateValidator().ValidateAsync(new CheckRequest(new string('a', 100), "http://10.0.0.1/a.png"));

        Assert.False(outcome.IsValid);
        Assert.Contains("text", outcome.Errors.Keys);
        Assert.Contains("imageUrl", outcome.Errors.Keys);
    }
}
