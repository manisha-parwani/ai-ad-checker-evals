using AdChecker.Core.Analysis;
using AdChecker.Core.Decisions;
using AdChecker.Core.Model;

namespace AdChecker.Core;

/// <summary>Runs the analyzer on every input of an ad and applies the decision policy.</summary>
public sealed class AdCheckService(IContentAnalyzer analyzer, DecisionPolicy policy)
{
    public string AnalyzerName => analyzer.Name;

    /// <summary>Inputs must already have passed the guardrails.</summary>
    public async Task<CheckResult> CheckAsync(string? text, Uri? imageUrl, CancellationToken cancellationToken = default)
    {
        var inputs = new List<AnalysisInput>(2);
        if (!string.IsNullOrWhiteSpace(text))
        {
            inputs.Add(AnalysisInput.ForText(text));
        }

        if (imageUrl is not null)
        {
            inputs.Add(AnalysisInput.ForImage(imageUrl));
        }

        if (inputs.Count == 0)
        {
            throw new ArgumentException("Give text, an image URL, or both.");
        }

        var results = await Task.WhenAll(inputs.Select(async input =>
            (input.Kind, await analyzer.AnalyzeAsync(input, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);

        return policy.Combine(results);
    }
}
