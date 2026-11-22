using AdChecker.Core.Model;

namespace AdChecker.Evals;

/// <summary>One hand-written test case from evals/testset.json.</summary>
public sealed record EvalCase(
    string Id,
    string Text,
    Decision ExpectedDecision,
    RiskCategory ExpectedCategory,
    bool MustPass,
    string? Note = null);

public sealed record TestSet(IReadOnlyList<EvalCase> Cases, string? Description = null);

/// <summary>Result of one run of one case. <see cref="Error"/> is set when the analyzer failed.</summary>
public sealed record RunOutcome(
    Decision? Decision,
    IReadOnlyList<RiskCategory> Categories,
    double? Confidence,
    string? Error = null)
{
    public static RunOutcome Failed(string error) => new(null, [], null, error);
}

/// <summary>All runs of one case.</summary>
public sealed record CaseResult(EvalCase Case, IReadOnlyList<RunOutcome> Runs)
{
    public int PassedRuns => Runs.Count(r => EvalScorer.IsPass(Case, r));

    public bool AllRunsPassed => Runs.Count > 0 && PassedRuns == Runs.Count;

    /// <summary>True when the decision was not the same in every run.</summary>
    public bool IsFlaky => Runs.Select(r => r.Decision).Distinct().Count() > 1;

    /// <summary>Share of runs that gave the most common decision (1.0 = always the same).</summary>
    public double DecisionAgreement => EvalScorer.Agreement(Runs.Select(r => r.Decision).ToArray());

    public double ConfidenceStdDev => EvalScorer.PopulationStdDev(Runs.Where(r => r.Confidence is not null).Select(r => r.Confidence!.Value).ToArray());
}

/// <summary>Detection numbers for one risk category, counted per run.</summary>
public sealed record CategoryStats(
    RiskCategory Category,
    int RiskyRuns,
    int Caught,
    int Missed,
    int CleanRunsWronglyRejected,
    int CleanRunsSentToReview);

public sealed record EvalSummary(
    int CaseCount,
    int RunsPerCase,
    int TotalRuns,
    int PassedRuns,
    double PassRate,
    int CasesAllRunsPassed,
    int MustPassCaseCount,
    IReadOnlyList<CaseResult> MustPassFailures,
    IReadOnlyList<CaseResult> FailedCases,
    int RiskyRunsApproved,
    int CleanRunsRejected,
    int CleanRunsSentToReview,
    int ErrorRuns,
    IReadOnlyList<CategoryStats> PerCategory,
    IReadOnlyList<CaseResult> FlakyCases,
    double MeanDecisionAgreement,
    double MeanConfidenceStdDev,
    double MaxConfidenceStdDev)
{
    public bool Passed => MustPassFailures.Count == 0;
}
