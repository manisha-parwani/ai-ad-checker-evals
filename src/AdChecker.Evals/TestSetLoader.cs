using System.Text.Json;
using AdChecker.Core;
using AdChecker.Core.Model;

namespace AdChecker.Evals;

public static class TestSetLoader
{
    // Stricter than the API defaults: every case field except "note" must be present and non-null.
    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static async Task<TestSet> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        TestSet? testSet;
        try
        {
            testSet = await JsonSerializer.DeserializeAsync<TestSet>(stream, Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a valid test set: {ex.Message}", ex);
        }

        if (testSet?.Cases is null || testSet.Cases.Count == 0)
        {
            throw new InvalidDataException($"{path} has no cases.");
        }

        Validate(testSet);
        return testSet;
    }

    /// <summary>Catches mistakes in hand-written cases before they turn into confusing scores.</summary>
    public static void Validate(TestSet testSet)
    {
        ArgumentNullException.ThrowIfNull(testSet);
        var problems = new List<string>();

        foreach (var duplicate in testSet.Cases.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"duplicate id '{duplicate.Key}'");
        }

        foreach (var c in testSet.Cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
            {
                problems.Add("a case has no id");
            }

            if (string.IsNullOrWhiteSpace(c.Text))
            {
                problems.Add($"'{c.Id}' has no text");
            }

            if (c.ExpectedDecision == Decision.Approved && c.ExpectedCategory != RiskCategory.None)
            {
                problems.Add($"'{c.Id}' is expected approved, so expectedCategory must be 'none'");
            }

            if (c.ExpectedDecision == Decision.Rejected && c.ExpectedCategory == RiskCategory.None)
            {
                problems.Add($"'{c.Id}' is expected rejected, so it needs an expectedCategory");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("Invalid test set: " + string.Join("; ", problems) + ".");
        }
    }
}
