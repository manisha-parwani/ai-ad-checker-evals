namespace AdChecker.Core.Model;

/// <summary>
/// What an analyzer said about one input.
/// </summary>
/// <param name="Categories">Risk categories found. Empty means "none".</param>
/// <param name="Confidence">0..1, or null when the analyzer gave no usable confidence.
/// Null is treated as "not sure" and sends the ad to human review.</param>
/// <param name="Reason">Short human-readable reason.</param>
public sealed record AnalyzerResult(
    IReadOnlyList<RiskCategory> Categories,
    double? Confidence,
    string Reason)
{
    public static AnalyzerResult Clean(double confidence, string reason) => new([], confidence, reason);
}
