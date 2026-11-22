using AdChecker.Core.Model;

namespace AdChecker.Core.Analysis;

/// <summary>
/// Classifies one ad input into brand-safety risk categories.
/// The real implementation calls Azure AI Content Understanding; the fake one uses keyword rules.
/// </summary>
public interface IContentAnalyzer
{
    /// <summary>Short name shown in API responses and eval reports, for example "fake".</summary>
    string Name { get; }

    /// <exception cref="AnalyzerException">The analyzer could not produce a result.</exception>
    Task<AnalyzerResult> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken = default);
}

/// <summary>The analyzer failed (network error, service error, timeout, bad response).</summary>
public sealed class AnalyzerException : Exception
{
    public AnalyzerException()
    {
    }

    public AnalyzerException(string message)
        : base(message)
    {
    }

    public AnalyzerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
