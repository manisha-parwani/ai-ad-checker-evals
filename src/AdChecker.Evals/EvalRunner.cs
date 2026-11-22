using AdChecker.Core;
using AdChecker.Core.Analysis;

namespace AdChecker.Evals;

/// <summary>Runs every case N times through the same pipeline the API uses (analyzer + decision policy).</summary>
public sealed class EvalRunner(AdCheckService service)
{
    public async Task<IReadOnlyList<CaseResult>> RunAsync(
        IReadOnlyList<EvalCase> cases,
        int runsPerCase,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentOutOfRangeException.ThrowIfLessThan(runsPerCase, 1);

        var results = new List<CaseResult>(cases.Count);
        foreach (var evalCase in cases)
        {
            var runs = new List<RunOutcome>(runsPerCase);
            for (var i = 0; i < runsPerCase; i++)
            {
                try
                {
                    var result = await service.CheckAsync(evalCase.Text, null, cancellationToken);
                    runs.Add(new RunOutcome(result.Decision, result.Categories, result.Confidence));
                }
                catch (AnalyzerException ex)
                {
                    runs.Add(RunOutcome.Failed(ex.Message));
                }
            }

            var caseResult = new CaseResult(evalCase, runs);
            results.Add(caseResult);
            log?.Invoke($"{(caseResult.AllRunsPassed ? "PASS" : "FAIL"),-4} {evalCase.Id,-14} {caseResult.PassedRuns}/{runsPerCase} runs" +
                        (evalCase.MustPass && !caseResult.AllRunsPassed ? "  <-- MUST PASS" : string.Empty));
        }

        return results;
    }
}
