using AdChecker.Core.Model;
using AdChecker.Evals;

namespace AdChecker.Tests;

public class EvalScorerTests
{
    private static readonly EvalCase CleanCase = new("clean", "Fresh bread.", Decision.Approved, RiskCategory.None, MustPass: true);
    private static readonly EvalCase CasinoCase = new("casino", "Free spins.", Decision.Rejected, RiskCategory.Gambling, MustPass: true);
    private static readonly EvalCase KnifeCase = new("knife", "Tactical knives.", Decision.NeedsReview, RiskCategory.Weapons, MustPass: false);

    private static RunOutcome Run(Decision decision, double confidence, params RiskCategory[] categories) => new(decision, categories, confidence);

    [Fact]
    public void Run_passes_when_decision_and_category_match()
    {
        Assert.True(EvalScorer.IsPass(CasinoCase, Run(Decision.Rejected, 0.9, RiskCategory.Gambling)));
        Assert.True(EvalScorer.IsPass(CasinoCase, Run(Decision.Rejected, 0.9, RiskCategory.Gambling, RiskCategory.Alcohol)));
        Assert.True(EvalScorer.IsPass(CleanCase, Run(Decision.Approved, 0.9)));
    }

    [Fact]
    public void Right_decision_with_the_wrong_category_fails() =>
        Assert.False(EvalScorer.IsPass(CasinoCase, Run(Decision.Rejected, 0.9, RiskCategory.Alcohol)));

    [Fact]
    public void Wrong_decision_fails() =>
        Assert.False(EvalScorer.IsPass(KnifeCase, Run(Decision.Rejected, 0.9, RiskCategory.Weapons)));

    [Fact]
    public void Analyzer_error_fails() =>
        Assert.False(EvalScorer.IsPass(CleanCase, RunOutcome.Failed("timeout")));

    [Fact]
    public void Population_std_dev_is_correct()
    {
        // mean 0.7, squared deviations 0.04 + 0 + 0.04, / 3 = 0.02667, sqrt = 0.16330
        Assert.Equal(0.16330, EvalScorer.PopulationStdDev([0.5, 0.7, 0.9]), precision: 5);
        Assert.Equal(0, EvalScorer.PopulationStdDev([0.8, 0.8, 0.8]), precision: 10);
        Assert.Equal(0, EvalScorer.PopulationStdDev([0.8]));
        Assert.Equal(0, EvalScorer.PopulationStdDev([]));
    }

    [Fact]
    public void Agreement_is_the_share_of_the_most_common_decision()
    {
        Assert.Equal(1.0, EvalScorer.Agreement([Decision.Rejected, Decision.Rejected, Decision.Rejected]));
        Assert.Equal(2 / 3.0, EvalScorer.Agreement([Decision.Rejected, Decision.Rejected, Decision.Approved]), precision: 10);
        Assert.Equal(0.5, EvalScorer.Agreement([Decision.Approved, null]));
    }

    [Fact]
    public void Summary_counts_pass_rate_must_pass_failures_and_categories()
    {
        var results = new[]
        {
            // clean ad: 2 of 3 runs wrongly rejected for alcohol
            new CaseResult(CleanCase, [Run(Decision.Approved, 0.9), Run(Decision.Rejected, 0.8, RiskCategory.Alcohol), Run(Decision.Rejected, 0.8, RiskCategory.Alcohol)]),
            // casino: caught twice, let through once
            new CaseResult(CasinoCase, [Run(Decision.Rejected, 0.9, RiskCategory.Gambling), Run(Decision.Rejected, 0.95, RiskCategory.Gambling), Run(Decision.Approved, 0.75)]),
            // knife: always correct
            new CaseResult(KnifeCase, [Run(Decision.NeedsReview, 0.5, RiskCategory.Weapons), Run(Decision.NeedsReview, 0.5, RiskCategory.Weapons), Run(Decision.NeedsReview, 0.5, RiskCategory.Weapons)]),
        };

        var summary = EvalScorer.Summarize(results, runsPerCase: 3);

        Assert.Equal(9, summary.TotalRuns);
        Assert.Equal(6, summary.PassedRuns); // 1 + 2 + 3
        Assert.Equal(6 / 9.0, summary.PassRate, precision: 10);
        Assert.Equal(1, summary.CasesAllRunsPassed);
        Assert.Equal(2, summary.MustPassCaseCount);
        Assert.Equal(["clean", "casino"], summary.MustPassFailures.Select(f => f.Case.Id));
        Assert.False(summary.Passed);

        Assert.Equal(1, summary.RiskyRunsApproved);
        Assert.Equal(2, summary.CleanRunsRejected);
        Assert.Equal(0, summary.CleanRunsSentToReview);

        var gambling = summary.PerCategory.Single(c => c.Category == RiskCategory.Gambling);
        Assert.Equal((3, 2, 1), (gambling.RiskyRuns, gambling.Caught, gambling.Missed));

        var weapons = summary.PerCategory.Single(c => c.Category == RiskCategory.Weapons);
        Assert.Equal((3, 3, 0), (weapons.RiskyRuns, weapons.Caught, weapons.Missed));

        var alcohol = summary.PerCategory.Single(c => c.Category == RiskCategory.Alcohol);
        Assert.Equal((0, 2, 0), (alcohol.RiskyRuns, alcohol.CleanRunsWronglyRejected, alcohol.CleanRunsSentToReview));

        Assert.DoesNotContain(summary.PerCategory, c => c.Category == RiskCategory.None);
    }

    [Fact]
    public void Summary_reports_variance()
    {
        var results = new[]
        {
            new CaseResult(CasinoCase, [Run(Decision.Rejected, 0.9, RiskCategory.Gambling), Run(Decision.Rejected, 0.7, RiskCategory.Gambling), Run(Decision.NeedsReview, 0.5, RiskCategory.Gambling)]),
            new CaseResult(CleanCase, [Run(Decision.Approved, 0.9), Run(Decision.Approved, 0.9), Run(Decision.Approved, 0.9)]),
        };

        var summary = EvalScorer.Summarize(results, runsPerCase: 3);

        Assert.Equal(["casino"], summary.FlakyCases.Select(f => f.Case.Id));
        Assert.Equal((2 / 3.0 + 1) / 2, summary.MeanDecisionAgreement, precision: 10);
        Assert.Equal(0.16330, summary.MaxConfidenceStdDev, precision: 5);
        Assert.Equal(0.16330 / 2, summary.MeanConfidenceStdDev, precision: 5);
    }

    [Fact]
    public void Errors_are_counted_and_fail_the_case()
    {
        var results = new[] { new CaseResult(CleanCase, [Run(Decision.Approved, 0.9), RunOutcome.Failed("HTTP 500")]) };

        var summary = EvalScorer.Summarize(results, runsPerCase: 2);

        Assert.Equal(1, summary.ErrorRuns);
        Assert.Single(summary.MustPassFailures);
    }

    [Fact]
    public void Report_mentions_the_fake_analyzer_and_must_pass_failures()
    {
        var results = new[] { new CaseResult(CasinoCase, [Run(Decision.Approved, 0.9)]) };
        var summary = EvalScorer.Summarize(results, 1);

        var markdown = ReportWriter.Render(summary, results, new ReportContext("fake", "evals/testset.json", 0.7, DateTimeOffset.UnixEpoch, TimeSpan.Zero));

        Assert.Contains("NOT from the real AI service", markdown);
        Assert.Contains("FAIL: 1 must-pass case(s) failed", markdown);
        Assert.Contains("| casino | yes | rejected (gambling) | approved x1 | 0/1 |", markdown);
    }

    [Fact]
    public void Runs_are_described_compactly() =>
        Assert.Equal(
            "rejected (alcohol) x2, approved x1, error x1",
            ReportWriter.DescribeRuns([Run(Decision.Rejected, 0.9, RiskCategory.Alcohol), Run(Decision.Approved, 0.9), Run(Decision.Rejected, 0.8, RiskCategory.Alcohol), RunOutcome.Failed("x")]));
}
