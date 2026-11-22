namespace AdChecker.Core.Model;

/// <summary>The analyzer result for one input, plus the decision for that input alone.</summary>
public sealed record InputVerdict(InputKind Input, Decision Decision, AnalyzerResult Result);

/// <summary>Final result of a check, combined over all inputs (text and/or image).</summary>
public sealed record CheckResult(
    Decision Decision,
    IReadOnlyList<RiskCategory> Categories,
    double Confidence,
    string Reason,
    IReadOnlyList<InputVerdict> Inputs);
