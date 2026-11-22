using System.Text.Json;
using AdChecker.Core.Model;

namespace AdChecker.Core.Analysis.ContentUnderstanding;

/// <summary>
/// Maps a finished Content Understanding analyze result to an <see cref="AnalyzerResult"/>.
/// Field names match analyzer/brand-safety-analyzer.json. Anything unexpected (missing fields,
/// unknown labels, confidence outside 0..1) gives a null confidence, which sends the ad to human review.
/// </summary>
public static class ContentUnderstandingResultParser
{
    public const string PrimaryCategoryField = "PrimaryCategory";
    public const string OtherCategoriesField = "OtherCategories";
    public const string ConfidenceScoreField = "ConfidenceScore";
    public const string ReasonField = "Reason";

    public static AnalyzerResult Parse(JsonElement operation)
    {
        if (!TryGetFields(operation, out var fields))
        {
            return new AnalyzerResult([], null, "Content Understanding returned no fields.");
        }

        var problems = new List<string>();
        var categories = new SortedSet<RiskCategory>();

        var primaryLabel = GetString(fields, PrimaryCategoryField);
        if (RiskCategories.TryParse(primaryLabel, out var primary))
        {
            if (primary != RiskCategory.None)
            {
                categories.Add(primary);
            }
        }
        else
        {
            problems.Add(primaryLabel is null ? "no primary category" : $"unknown category '{primaryLabel}'");
        }

        foreach (var label in GetStringArray(fields, OtherCategoriesField))
        {
            if (RiskCategories.TryParse(label, out var other))
            {
                if (other != RiskCategory.None)
                {
                    categories.Add(other);
                }
            }
            else
            {
                problems.Add($"unknown category '{label}'");
            }
        }

        // Prefer the score the model was asked for; fall back to the service's own field confidence, if any.
        var confidence = GetNumber(fields, ConfidenceScoreField) ?? GetFieldConfidence(fields, PrimaryCategoryField);
        if (confidence is { } value && (double.IsNaN(value) || value < 0 || value > 1))
        {
            problems.Add($"confidence {value} is outside 0..1");
            confidence = null;
        }

        var reason = GetString(fields, ReasonField)?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            reason = "No reason given.";
        }

        if (problems.Count > 0)
        {
            return new AnalyzerResult(categories.ToArray(), null, $"Unexpected analyzer output ({string.Join("; ", problems)}). {reason}");
        }

        return new AnalyzerResult(categories.ToArray(), confidence, reason);
    }

    private static bool TryGetFields(JsonElement operation, out JsonElement fields)
    {
        fields = default;
        if (!operation.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("contents", out var contents) ||
            contents.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var content in contents.EnumerateArray())
        {
            if (content.TryGetProperty("fields", out fields) && fields.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var field) &&
        field.TryGetProperty("valueString", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetNumber(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var field))
        {
            return null;
        }

        foreach (var property in new[] { "valueNumber", "valueInteger" })
        {
            if (field.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }
        }

        return null;
    }

    private static double? GetFieldConfidence(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var field) &&
        field.TryGetProperty("confidence", out var value) &&
        value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static IEnumerable<string> GetStringArray(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var field) ||
            !field.TryGetProperty("valueArray", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("valueString", out var value) && value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                yield return value.GetString()!;
            }
        }
    }
}
