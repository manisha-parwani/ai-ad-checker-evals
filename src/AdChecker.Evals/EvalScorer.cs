using AdChecker.Core.Model;

namespace AdChecker.Evals;

/// <summary>Scoring maths. Pure functions, unit tested.</summary>
public static class EvalScorer
{
    /// <summary>
    /// A run passes when the decision matches the expected decision and, for a risky case,
    /// the expected category is among the returned categories. A failed analyzer call never passes.
    /// </summary>
    public static bool IsPass(EvalCase evalCase, RunOutcome run)
    {
        ArgumentNullException.ThrowIfNull(evalCase);
        ArgumentNullException.ThrowIfNull(run);

        return run.Error is null &&
               run.Decision == evalCase.ExpectedDecision &&
               (evalCase.ExpectedCategory == RiskCategory.None || run.Categories.Contains(evalCase.ExpectedCategory));
    }

    /// <summary>Population standard deviation. 0 for fewer than two values.</summary>
    public static double PopulationStdDev(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    /// <summary>Share of runs with the most common decision. Failed runs (null) count as their own "decision".</summary>
    public static double Agreement(IReadOnlyCollection<Decision?> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (decisions.Count == 0)
        {
            return 1;
        }

        return decisions.GroupBy(d => d).Max(g => g.Count()) / (double)decisions.Count;
    }

    public static EvalSummary Summarize(IReadOnlyList<CaseResult> results, int runsPerCase)
    {
        ArgumentNullException.ThrowIfNull(results);

        var allRuns = results.SelectMany(r => r.Runs.Select(run => (r.Case, Run: run))).ToArray();
        var passed = allRuns.Count(x => IsPass(x.Case, x.Run));
        var cleanRuns = allRuns.Where(x => x.Case.ExpectedDecision == Decision.Approved).ToArray();
        var riskyRuns = allRuns.Where(x => x.Case.ExpectedDecision != Decision.Approved).ToArray();

        var perCategory = RiskCategories.Risky
            .Select(category =>
            {
                var expected = allRuns.Where(x => x.Case.ExpectedCategory == category).ToArray();
                var caught = expected.Count(x => IsCaught(x.Run, category));
                return new CategoryStats(
                    category,
                    RiskyRuns: expected.Length,
                    Caught: caught,
                    Missed: expected.Length - caught,
                    CleanRunsWronglyRejected: cleanRuns.Count(x => x.Run.Decision == Decision.Rejected && x.Run.Categories.Contains(category)),
                    CleanRunsSentToReview: cleanRuns.Count(x => x.Run.Decision == Decision.NeedsReview && x.Run.Categories.Contains(category)));
            })
            .ToArray();

        var stdDevs = results.Select(r => r.ConfidenceStdDev).ToArray();

        return new EvalSummary(
            CaseCount: results.Count,
            RunsPerCase: runsPerCase,
            TotalRuns: allRuns.Length,
            PassedRuns: passed,
            PassRate: allRuns.Length == 0 ? 0 : passed / (double)allRuns.Length,
            CasesAllRunsPassed: results.Count(r => r.AllRunsPassed),
            MustPassCaseCount: results.Count(r => r.Case.MustPass),
            MustPassFailures: results.Where(r => r.Case.MustPass && !r.AllRunsPassed).ToArray(),
            FailedCases: results.Where(r => !r.AllRunsPassed).ToArray(),
            RiskyRunsApproved: riskyRuns.Count(x => x.Run.Decision == Decision.Approved),
            CleanRunsRejected: cleanRuns.Count(x => x.Run.Decision == Decision.Rejected),
            CleanRunsSentToReview: cleanRuns.Count(x => x.Run.Decision == Decision.NeedsReview),
            ErrorRuns: allRuns.Count(x => x.Run.Error is not null),
            PerCategory: perCategory,
            FlakyCases: results.Where(r => r.IsFlaky).ToArray(),
            MeanDecisionAgreement: results.Count == 0 ? 1 : results.Average(r => r.DecisionAgreement),
            MeanConfidenceStdDev: stdDevs.Length == 0 ? 0 : stdDevs.Average(),
            MaxConfidenceStdDev: stdDevs.Length == 0 ? 0 : stdDevs.Max());
    }

    /// <summary>The ad was not approved and the category was flagged.</summary>
    private static bool IsCaught(RunOutcome run, RiskCategory category) =>
        run.Error is null && run.Decision is not null and not Decision.Approved && run.Categories.Contains(category);
}
