using AdChecker.Core.Decisions;
using AdChecker.Core.Guardrails;

namespace AdChecker;

public enum AnalyzerKind
{
    /// <summary>Keyword rules. No Azure needed.</summary>
    Fake,

    /// <summary>Azure AI Content Understanding with Microsoft Entra ID auth.</summary>
    ContentUnderstanding,
}

/// <summary>Bound from the "AdChecker" configuration section.</summary>
public sealed class AdCheckerSettings
{
    public const string SectionName = "AdChecker";

    public AnalyzerKind Analyzer { get; set; } = AnalyzerKind.Fake;

    /// <summary>Results with confidence below this value go to human review.</summary>
    public double ReviewConfidenceThreshold { get; set; } = DecisionPolicy.DefaultReviewThreshold;

    public GuardrailOptions Guardrails { get; set; } = new();
}
