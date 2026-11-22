using AdChecker.Core.Model;

namespace AdChecker.Core.Decisions;

/// <summary>
/// Turns analyzer results into a decision.
/// <list type="number">
/// <item>Confidence below the threshold (or missing) means a person must decide: <see cref="Decision.NeedsReview"/>.</item>
/// <item>Otherwise, any risk category means <see cref="Decision.Rejected"/>.</item>
/// <item>Otherwise the ad is <see cref="Decision.Approved"/>.</item>
/// </list>
/// With several inputs (text and image) the most severe decision wins: rejected, then needs_review, then approved.
/// A confident rejection of one input is not softened by an unsure result for another input.
/// </summary>
public sealed class DecisionPolicy
{
    public const double DefaultReviewThreshold = 0.7;

    public DecisionPolicy(double reviewThreshold = DefaultReviewThreshold)
    {
        if (double.IsNaN(reviewThreshold) || reviewThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewThreshold), reviewThreshold, "Must be between 0 and 1.");
        }

        ReviewThreshold = reviewThreshold;
    }

    public double ReviewThreshold { get; }

    public Decision Decide(AnalyzerResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!IsConfident(result.Confidence))
        {
            return Decision.NeedsReview;
        }

        return result.Categories.Count > 0 ? Decision.Rejected : Decision.Approved;
    }

    public CheckResult Combine(IReadOnlyList<(InputKind Input, AnalyzerResult Result)> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            throw new ArgumentException("At least one analyzer result is needed.", nameof(results));
        }

        var verdicts = results.Select(r => new InputVerdict(r.Input, Decide(r.Result), r.Result)).ToArray();
        var decision = verdicts.Select(v => v.Decision).MaxBy(Severity);
        var deciding = verdicts.Where(v => v.Decision == decision).ToArray();

        // Report the confidence of the inputs that drove the decision.
        // Rejected: the strongest rejection. Review or approved: the weakest link.
        var confidences = deciding.Select(v => Clamp(v.Result.Confidence));
        var confidence = decision == Decision.Rejected ? confidences.Max() : confidences.Min();

        var categories = verdicts.SelectMany(v => v.Result.Categories).Distinct().Order().ToArray();
        var reason = deciding.Length == 1 && verdicts.Length == 1
            ? deciding[0].Result.Reason
            : string.Join(" ", deciding.Select(v => $"[{v.Input.ToString().ToLowerInvariant()}] {v.Result.Reason}"));

        return new CheckResult(decision, categories, Math.Round(confidence, 3), reason, verdicts);
    }

    private bool IsConfident(double? confidence) =>
        confidence is { } value && !double.IsNaN(value) && value >= ReviewThreshold;

    private static double Clamp(double? confidence) =>
        confidence is { } value && !double.IsNaN(value) ? Math.Clamp(value, 0, 1) : 0;

    private static int Severity(Decision decision) => decision switch
    {
        Decision.Rejected => 2,
        Decision.NeedsReview => 1,
        _ => 0,
    };
}
