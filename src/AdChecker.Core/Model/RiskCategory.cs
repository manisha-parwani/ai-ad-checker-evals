namespace AdChecker.Core.Model;

/// <summary>Brand-safety risk categories. Serialized in snake_case, for example "misleading_claims".</summary>
public enum RiskCategory
{
    None,
    Violence,
    Adult,
    Hate,
    Weapons,
    Alcohol,
    Gambling,
    MisleadingClaims,
}

public static class RiskCategories
{
    /// <summary>All categories except <see cref="RiskCategory.None"/>.</summary>
    public static IReadOnlyList<RiskCategory> Risky { get; } =
        Enum.GetValues<RiskCategory>().Where(c => c != RiskCategory.None).ToArray();

    /// <summary>The wire name of a category, for example "misleading_claims".</summary>
    public static string ToWireName(this RiskCategory category) => category switch
    {
        RiskCategory.MisleadingClaims => "misleading_claims",
        _ => category.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Parses a category label from a model or a file. Accepts "misleading_claims",
    /// "misleading claims" and "Misleading-Claims". Returns false for unknown labels.
    /// </summary>
    public static bool TryParse(string? label, out RiskCategory category)
    {
        category = RiskCategory.None;
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var normalized = label.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        foreach (var candidate in Enum.GetValues<RiskCategory>())
        {
            if (candidate.ToWireName() == normalized)
            {
                category = candidate;
                return true;
            }
        }

        return false;
    }
}
