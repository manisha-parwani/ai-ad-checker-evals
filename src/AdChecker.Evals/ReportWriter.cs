using System.Globalization;
using System.Text;
using AdChecker.Core.Model;

namespace AdChecker.Evals;

public sealed record ReportContext(
    string AnalyzerName,
    string TestSetPath,
    double ReviewThreshold,
    DateTimeOffset GeneratedAt,
    TimeSpan Duration);

/// <summary>Writes the eval summary as a Markdown report.</summary>
public static class ReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(EvalSummary s, IReadOnlyList<CaseResult> results, ReportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(ctx);

        var md = new StringBuilder();
        md.AppendLine("# Eval report: brand-safety ad checker").AppendLine();

        if (ctx.AnalyzerName == "fake")
        {
            md.AppendLine("> **Analyzer: `fake`**. These are results from the deterministic keyword-rule analyzer, NOT from the real AI service.")
              .AppendLine("> They show how the eval harness works and where simple rules fail. They say nothing about the quality of Azure AI Content Understanding.")
              .AppendLine();
        }
        else
        {
            md.AppendLine($"> **Analyzer: `{ctx.AnalyzerName}`** (live AI service). AI answers can change between runs and over time, so compare several reports, not one.")
              .AppendLine();
        }

        md.AppendLine("| | |").AppendLine("|---|---|")
          .AppendLine($"| Result | {(s.Passed ? "PASS: no must-pass failures" : $"FAIL: {s.MustPassFailures.Count} must-pass case(s) failed")} |")
          .AppendLine($"| Test set | `{ctx.TestSetPath}` ({s.CaseCount} text cases, {s.MustPassCaseCount} must-pass) |")
          .AppendLine($"| Runs per case | {s.RunsPerCase} ({s.TotalRuns} runs in total) |")
          .AppendLine($"| Review threshold | confidence below {ctx.ReviewThreshold.ToString("0.00", Inv)} goes to human review |")
          .AppendLine($"| Generated | {ctx.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", Inv)} UTC, took {ctx.Duration.TotalSeconds.ToString("0.0", Inv)} s |")
          .AppendLine();

        md.AppendLine("## Summary").AppendLine()
          .AppendLine("| Metric | Value |").AppendLine("|---|---|")
          .AppendLine($"| Overall pass rate (all runs) | **{Percent(s.PassRate)}** ({s.PassedRuns} / {s.TotalRuns}) |")
          .AppendLine($"| Cases that passed in every run | {s.CasesAllRunsPassed} / {s.CaseCount} |")
          .AppendLine($"| Must-pass cases that failed | {s.MustPassFailures.Count} / {s.MustPassCaseCount} |")
          .AppendLine($"| Risky-ad runs approved (let through) | {s.RiskyRunsApproved} |")
          .AppendLine($"| Clean-ad runs wrongly rejected | {s.CleanRunsRejected} |")
          .AppendLine($"| Clean-ad runs sent to human review | {s.CleanRunsSentToReview} |")
          .AppendLine($"| Analyzer errors | {s.ErrorRuns} |")
          .AppendLine();

        md.AppendLine("## Per category").AppendLine()
          .AppendLine("Counted per run (cases x runs). **Caught** = the ad was not approved and the expected category was flagged. " +
                      "The last two columns count clean ads that were flagged with this category.")
          .AppendLine()
          .AppendLine("| Category | Risky runs | Caught | Missed | Clean ads wrongly rejected | Clean ads sent to review |")
          .AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var c in s.PerCategory)
        {
            md.AppendLine($"| {c.Category.ToWireName()} | {c.RiskyRuns} | {c.Caught} | {c.Missed} | {c.CleanRunsWronglyRejected} | {c.CleanRunsSentToReview} |");
        }

        md.AppendLine();

        md.AppendLine("## Must-pass failures").AppendLine();
        if (s.MustPassFailures.Count == 0)
        {
            md.AppendLine("None. Every must-pass case passed in every run.");
        }
        else
        {
            AppendCaseTable(md, s.MustPassFailures);
        }

        md.AppendLine();

        md.AppendLine("## All failed cases").AppendLine();
        if (s.FailedCases.Count == 0)
        {
            md.AppendLine("None.");
        }
        else
        {
            AppendCaseTable(md, s.FailedCases);
        }

        md.AppendLine();

        md.AppendLine("## Run-to-run variance").AppendLine()
          .AppendLine("| Metric | Value |").AppendLine("|---|---|")
          .AppendLine($"| Cases whose decision changed between runs | {s.FlakyCases.Count} / {s.CaseCount} |")
          .AppendLine($"| Mean decision agreement (share of runs with the most common decision) | {Percent(s.MeanDecisionAgreement)} |")
          .AppendLine($"| Mean confidence std dev per case | {s.MeanConfidenceStdDev.ToString("0.000", Inv)} |")
          .AppendLine($"| Max confidence std dev per case | {s.MaxConfidenceStdDev.ToString("0.000", Inv)} |")
          .AppendLine();
        if (ctx.AnalyzerName == "fake")
        {
            md.AppendLine("The fake analyzer is deterministic, so zero variance is expected here. With a real AI model, expect some cases to change between runs.").AppendLine();
        }

        if (s.FlakyCases.Count > 0)
        {
            md.AppendLine("Flaky cases:").AppendLine();
            AppendCaseTable(md, s.FlakyCases);
            md.AppendLine();
        }

        md.AppendLine("## How a run is scored").AppendLine()
          .AppendLine("- A run **passes** when the decision equals `expectedDecision` and, for risky cases, `expectedCategory` is one of the returned categories.")
          .AppendLine("- A case with `mustPass: true` must pass in **every** run. If one fails, the eval runner exits with code 1 and CI fails.")
          .AppendLine("- An analyzer error counts as a failed run.");

        return md.ToString();
    }

    private static void AppendCaseTable(StringBuilder md, IEnumerable<CaseResult> cases)
    {
        md.AppendLine("| Case | Must pass | Expected | Got (per run) | Passed runs | Ad text | Why it is tricky |")
          .AppendLine("|---|---|---|---|---:|---|---|");
        foreach (var r in cases)
        {
            var expected = r.Case.ExpectedCategory == RiskCategory.None
                ? Wire(r.Case.ExpectedDecision)
                : $"{Wire(r.Case.ExpectedDecision)} ({r.Case.ExpectedCategory.ToWireName()})";
            md.AppendLine($"| {Cell(r.Case.Id)} | {(r.Case.MustPass ? "yes" : "no")} | {Cell(expected)} | {Cell(DescribeRuns(r.Runs))} | {r.PassedRuns}/{r.Runs.Count} | {Cell(Shorten(r.Case.Text, 70))} | {Cell(r.Case.Note ?? string.Empty)} |");
        }
    }

    /// <summary>For example "needs_review (violence) x3" or "rejected (alcohol) x2, approved x1".</summary>
    public static string DescribeRuns(IEnumerable<RunOutcome> runs) =>
        string.Join(", ", runs
            .Select(DescribeRun)
            .GroupBy(d => d)
            .Select(g => $"{g.Key} x{g.Count()}"));

    private static string DescribeRun(RunOutcome run)
    {
        if (run.Error is not null)
        {
            return "error";
        }

        var decision = run.Decision is { } d ? Wire(d) : "?";
        return run.Categories.Count == 0 ? decision : $"{decision} ({string.Join(", ", run.Categories.Select(c => c.ToWireName()))})";
    }

    private static string Wire(Decision decision) => decision switch
    {
        Decision.Approved => "approved",
        Decision.Rejected => "rejected",
        Decision.NeedsReview => "needs_review",
        _ => decision.ToString(),
    };

    private static string Percent(double value) => (value * 100).ToString("0.0", Inv) + "%";

    private static string Shorten(string text, int max) => text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 3), "...");

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
