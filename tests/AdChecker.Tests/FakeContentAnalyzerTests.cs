using AdChecker.Core.Analysis;
using AdChecker.Core.Decisions;
using AdChecker.Core.Model;

namespace AdChecker.Tests;

public class FakeContentAnalyzerTests
{
    private readonly FakeContentAnalyzer _analyzer = new();

    [Fact]
    public void Clean_text_has_no_categories_and_is_confident()
    {
        var result = FakeContentAnalyzer.AnalyzeText("Fresh sourdough bread every morning.");

        Assert.Empty(result.Categories);
        Assert.True(result.Confidence >= DecisionPolicy.DefaultReviewThreshold);
    }

    [Theory]
    [InlineData("Free spins at our casino!", RiskCategory.Gambling)]
    [InlineData("Cheap vodka delivered tonight.", RiskCategory.Alcohol)]
    [InlineData("Buy a handgun online.", RiskCategory.Weapons)]
    [InlineData("Uncut massacre footage.", RiskCategory.Violence)]
    [InlineData("Free nude photos.", RiskCategory.Adult)]
    [InlineData("Join our hate group.", RiskCategory.Hate)]
    [InlineData("Miracle cure, no side effects!", RiskCategory.MisleadingClaims)]
    public void Strong_keyword_flags_its_category_with_high_confidence(string text, RiskCategory expected)
    {
        var result = FakeContentAnalyzer.AnalyzeText(text);

        Assert.Contains(expected, result.Categories);
        Assert.Equal(FakeContentAnalyzer.StrongMatchConfidence, result.Confidence);
    }

    [Fact]
    public void Only_weak_keywords_give_low_confidence()
    {
        var result = FakeContentAnalyzer.AnalyzeText("Tactical knives for campers.");

        Assert.Equal([RiskCategory.Weapons], result.Categories);
        Assert.True(result.Confidence < DecisionPolicy.DefaultReviewThreshold);
    }

    [Theory]
    [InlineData("Our killer app saves time.")] // "kill" is a whole word only
    [InlineData("Upgrade your skills today.")]
    [InlineData("Begin your journey.")] // contains "gin" inside a word
    [InlineData("A gunmetal grey bike.")] // contains "gun" inside a word
    public void Keywords_inside_other_words_do_not_match(string text) =>
        Assert.Empty(FakeContentAnalyzer.AnalyzeText(text).Categories);

    [Fact]
    public void Matching_ignores_case() =>
        Assert.Contains(RiskCategory.Gambling, FakeContentAnalyzer.AnalyzeText("ONLINE CASINO").Categories);

    [Fact]
    public void Several_categories_can_match_at_once()
    {
        var result = FakeContentAnalyzer.AnalyzeText("Free beer for every poker player.");

        Assert.Equal([RiskCategory.Alcohol, RiskCategory.Gambling], result.Categories);
        Assert.Contains("alcohol ('beer')", result.Reason);
    }

    [Fact]
    public async Task Same_input_always_gives_the_same_result()
    {
        var input = AnalysisInput.ForText("Bet now, best odds!");

        var first = await _analyzer.AnalyzeAsync(input);
        var second = await _analyzer.AnalyzeAsync(input);

        Assert.Equal(first.Categories, second.Categories);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.Reason, second.Reason);
    }

    [Fact]
    public async Task Images_always_go_to_human_review()
    {
        var result = await _analyzer.AnalyzeAsync(AnalysisInput.ForImage(new Uri("https://images.example.com/a.png")));

        Assert.Equal(Decision.NeedsReview, new DecisionPolicy().Decide(result));
        Assert.Contains("cannot look at images", result.Reason);
    }

    [Fact]
    public async Task Respects_cancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _analyzer.AnalyzeAsync(AnalysisInput.ForText("hi"), cts.Token));
    }
}
