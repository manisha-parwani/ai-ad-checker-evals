using AdChecker.Core.Model;

namespace AdChecker;

/// <summary>Response of <c>POST /check</c>.</summary>
public sealed record CheckResponse(
    Decision Decision,
    IReadOnlyList<RiskCategory> Categories,
    double Confidence,
    string Reason,
    string Analyzer,
    IReadOnlyList<InputCheckResponse> Inputs)
{
    public static CheckResponse From(CheckResult result, string analyzer) => new(
        result.Decision,
        result.Categories,
        result.Confidence,
        result.Reason,
        analyzer,
        result.Inputs.Select(i => new InputCheckResponse(i.Input, i.Decision, i.Result.Categories, i.Result.Confidence, i.Result.Reason)).ToArray());
}

/// <summary>Per-input detail, so a human reviewer can see whether the text or the image caused the decision.</summary>
public sealed record InputCheckResponse(
    InputKind Input,
    Decision Decision,
    IReadOnlyList<RiskCategory> Categories,
    double? Confidence,
    string Reason);
