using AdChecker.Core.Decisions;
using AdChecker.Core.Model;

namespace AdChecker.Tests;

public class DecisionPolicyTests
{
    private readonly DecisionPolicy _policy = new(reviewThreshold: 0.7);

    [Fact]
    public void Confident_and_flagged_is_rejected() =>
        Assert.Equal(Decision.Rejected, _policy.Decide(new([RiskCategory.Gambling], 0.95, "casino")));

    [Fact]
    public void One_flagged_category_is_enough_to_reject() =>
        Assert.Equal(Decision.Rejected, _policy.Decide(new([RiskCategory.Alcohol], 0.8, "beer")));

    [Fact]
    public void Confident_and_clean_is_approved() =>
        Assert.Equal(Decision.Approved, _policy.Decide(AnalyzerResult.Clean(0.9, "fine")));

    [Fact]
    public void Low_confidence_and_flagged_needs_review() =>
        Assert.Equal(Decision.NeedsReview, _policy.Decide(new([RiskCategory.Weapons], 0.5, "knives")));

    [Fact]
    public void Low_confidence_and_clean_needs_review() =>
        Assert.Equal(Decision.NeedsReview, _policy.Decide(AnalyzerResult.Clean(0.4, "not sure")));

    [Fact]
    public void Missing_confidence_needs_review() =>
        Assert.Equal(Decision.NeedsReview, _policy.Decide(new([], null, "no score")));

    [Fact]
    public void NaN_confidence_needs_review() =>
        Assert.Equal(Decision.NeedsReview, _policy.Decide(new([], double.NaN, "broken score")));

    [Fact]
    public void Confidence_exactly_at_the_threshold_counts_as_confident()
    {
        Assert.Equal(Decision.Approved, _policy.Decide(AnalyzerResult.Clean(0.7, "ok")));
        Assert.Equal(Decision.NeedsReview, _policy.Decide(AnalyzerResult.Clean(0.6999, "almost")));
    }

    [Fact]
    public void Confident_rejection_of_text_is_not_softened_by_an_unsure_image()
    {
        var result = _policy.Combine(
        [
            (InputKind.Text, new AnalyzerResult([RiskCategory.Weapons], 0.9, "gun")),
            (InputKind.Image, AnalyzerResult.Clean(0.2, "blurry")),
        ]);

        Assert.Equal(Decision.Rejected, result.Decision);
        Assert.Equal(0.9, result.Confidence);
        Assert.Equal([RiskCategory.Weapons], result.Categories);
        Assert.Contains("[text] gun", result.Reason);
    }

    [Fact]
    public void Unsure_image_sends_otherwise_clean_ad_to_review()
    {
        var result = _policy.Combine(
        [
            (InputKind.Text, AnalyzerResult.Clean(0.95, "fine")),
            (InputKind.Image, AnalyzerResult.Clean(0.3, "cannot tell")),
        ]);

        Assert.Equal(Decision.NeedsReview, result.Decision);
        Assert.Equal(0.3, result.Confidence);
        Assert.Equal(2, result.Inputs.Count);
        Assert.Equal(Decision.Approved, result.Inputs[0].Decision);
        Assert.Equal(Decision.NeedsReview, result.Inputs[1].Decision);
    }

    [Fact]
    public void Categories_from_all_inputs_are_merged_without_duplicates()
    {
        var result = _policy.Combine(
        [
            (InputKind.Text, new AnalyzerResult([RiskCategory.Gambling, RiskCategory.Alcohol], 0.9, "a")),
            (InputKind.Image, new AnalyzerResult([RiskCategory.Alcohol], 0.8, "b")),
        ]);

        Assert.Equal([RiskCategory.Alcohol, RiskCategory.Gambling], result.Categories);
    }

    [Fact]
    public void Approved_when_every_input_is_confident_and_clean()
    {
        var result = _policy.Combine(
        [
            (InputKind.Text, AnalyzerResult.Clean(0.95, "fine")),
            (InputKind.Image, AnalyzerResult.Clean(0.8, "fine too")),
        ]);

        Assert.Equal(Decision.Approved, result.Decision);
        Assert.Equal(0.8, result.Confidence); // weakest link
        Assert.Empty(result.Categories);
    }

    [Fact]
    public void Combine_needs_at_least_one_result() =>
        Assert.Throws<ArgumentException>(() => _policy.Combine([]));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Threshold_must_be_between_zero_and_one(double threshold) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionPolicy(threshold));
}
