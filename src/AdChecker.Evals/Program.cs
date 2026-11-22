using System.Diagnostics;
using System.Globalization;
using AdChecker.Core;
using AdChecker.Core.Analysis;
using AdChecker.Core.Analysis.ContentUnderstanding;
using AdChecker.Core.Decisions;
using AdChecker.Evals;
using Azure.Identity;

const int ExitOk = 0;
const int ExitMustPassFailed = 1;
const int ExitUsage = 2;

const string Usage = """
    Eval runner for the brand-safety ad checker.

    Usage: dotnet run --project src/AdChecker.Evals -- [options]

      --analyzer fake|cu   Analyzer to evaluate. Default: fake (no Azure needed).
                           'cu' uses Azure AI Content Understanding and needs:
                             ContentUnderstanding__Endpoint    (required)
                             ContentUnderstanding__AnalyzerId  (default: brandSafety)
                           and a signed-in identity ('az login').
      --runs N             Runs per case. Default: 3.
      --testset PATH       Test set. Default: evals/testset.json (searched upwards from here).
      --report PATH        Markdown report to write. Default: report.md next to the test set.
      --threshold X        Review confidence threshold, 0..1. Default: 0.7.

    Exit codes: 0 = all must-pass cases passed, 1 = a must-pass case failed, 2 = usage or setup error.
    """;

try
{
    var options = ParseArgs(args);
    if (options is null)
    {
        Console.WriteLine(Usage);
        return ExitOk;
    }

    var testSetPath = options.TestSet ?? FindUpwards(Path.Combine("evals", "testset.json"))
        ?? throw new ArgumentException("Could not find evals/testset.json. Pass --testset.");
    var reportPath = options.Report ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(testSetPath))!, "report.md");

    var testSet = await TestSetLoader.LoadAsync(testSetPath);
    var policy = new DecisionPolicy(options.Threshold);
    var analyzer = CreateAnalyzer(options.Analyzer);
    var service = new AdCheckService(analyzer, policy);

    Console.WriteLine($"Running {testSet.Cases.Count} cases x {options.Runs} runs with the '{analyzer.Name}' analyzer...");
    var clock = Stopwatch.StartNew();
    var results = await new EvalRunner(service).RunAsync(testSet.Cases, options.Runs, Console.WriteLine);
    clock.Stop();

    var summary = EvalScorer.Summarize(results, options.Runs);
    var context = new ReportContext(analyzer.Name, RelativeToRepo(testSetPath), options.Threshold, DateTimeOffset.UtcNow, clock.Elapsed);
    await File.WriteAllTextAsync(reportPath, ReportWriter.Render(summary, results, context));

    Console.WriteLine();
    Console.WriteLine($"Pass rate: {(summary.PassRate * 100).ToString("0.0", CultureInfo.InvariantCulture)}% ({summary.PassedRuns}/{summary.TotalRuns} runs), " +
                      $"must-pass failures: {summary.MustPassFailures.Count}/{summary.MustPassCaseCount}, flaky cases: {summary.FlakyCases.Count}");
    Console.WriteLine($"Report written to {reportPath}");

    if (!summary.Passed)
    {
        Console.Error.WriteLine($"FAILED: must-pass case(s) {string.Join(", ", summary.MustPassFailures.Select(f => f.Case.Id))}");
        return ExitMustPassFailed;
    }

    return ExitOk;
}
catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine("Run with --help for usage.");
    return ExitUsage;
}

static IContentAnalyzer CreateAnalyzer(string name) => name switch
{
    "fake" => new FakeContentAnalyzer(),
    "cu" => new ContentUnderstandingAnalyzer(
        ContentUnderstandingAnalyzer.CreateHttpClient(),
        new DefaultAzureCredential(),
        ContentUnderstandingOptionsFromEnvironment()),
    _ => throw new ArgumentException($"Unknown analyzer '{name}'. Use 'fake' or 'cu'."),
};

static ContentUnderstandingOptions ContentUnderstandingOptionsFromEnvironment()
{
    var options = new ContentUnderstandingOptions();
    var endpoint = Environment.GetEnvironmentVariable("ContentUnderstanding__Endpoint");
    options.Endpoint = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri : null;
    options.AnalyzerId = Environment.GetEnvironmentVariable("ContentUnderstanding__AnalyzerId") ?? options.AnalyzerId;
    options.ApiVersion = Environment.GetEnvironmentVariable("ContentUnderstanding__ApiVersion") ?? options.ApiVersion;
    return options;
}

static EvalOptions? ParseArgs(string[] args)
{
    var options = new EvalOptions();
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");

        switch (args[i])
        {
            case "-h" or "--help":
                return null;
            case "--analyzer":
                options.Analyzer = Next().ToLowerInvariant();
                break;
            case "--runs":
                options.Runs = int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var runs) && runs is >= 1 and <= 50
                    ? runs
                    : throw new ArgumentException("--runs must be a whole number from 1 to 50.");
                break;
            case "--testset":
                options.TestSet = Next();
                break;
            case "--report":
                options.Report = Next();
                break;
            case "--threshold":
                options.Threshold = double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t is >= 0 and <= 1
                    ? t
                    : throw new ArgumentException("--threshold must be a number from 0 to 1.");
                break;
            default:
                throw new ArgumentException($"Unknown option '{args[i]}'.");
        }
    }

    return options;
}

static string? FindUpwards(string relativePath)
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static string RelativeToRepo(string path)
{
    var full = Path.GetFullPath(path);
    var evalsDir = Path.GetDirectoryName(full);
    var repoRoot = evalsDir is null ? null : Path.GetDirectoryName(evalsDir);
    return repoRoot is null ? full : Path.GetRelativePath(repoRoot, full).Replace('\\', '/');
}

internal sealed class EvalOptions
{
    public string Analyzer { get; set; } = "fake";

    public int Runs { get; set; } = 3;

    public string? TestSet { get; set; }

    public string? Report { get; set; }

    public double Threshold { get; set; } = DecisionPolicy.DefaultReviewThreshold;
}
