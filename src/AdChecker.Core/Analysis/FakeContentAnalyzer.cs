using System.Text.RegularExpressions;
using AdChecker.Core.Model;

namespace AdChecker.Core.Analysis;

/// <summary>
/// Deterministic, keyword-based stand-in for the AI analyzer. Used for local runs, unit tests and CI,
/// so nothing needs Azure. It is intentionally simple: it does not understand context, slang,
/// negation or misspellings, and it cannot look at images. The eval report shows where that fails.
/// </summary>
public sealed class FakeContentAnalyzer : IContentAnalyzer
{
    /// <summary>Confidence when at least one strong keyword matched.</summary>
    public const double StrongMatchConfidence = 0.9;

    /// <summary>Confidence when only weak (ambiguous) keywords matched. Below the default review threshold.</summary>
    public const double WeakMatchConfidence = 0.55;

    /// <summary>Confidence when nothing matched.</summary>
    public const double NoMatchConfidence = 0.85;

    private static readonly IReadOnlyList<Rule> Rules =
    [
        new(RiskCategory.Violence,
            Strong: ["kill", "killing", "murder", "blood", "bloody", "bleed", "stab", "massacre", "gore", "torture", "beat up"],
            Weak: ["fight", "fighting", "shoot", "shooting", "attack", "brutal", "destroy", "revenge"]),
        new(RiskCategory.Adult,
            Strong: ["porn", "pornography", "xxx", "nude", "nudes", "naked", "erotic", "escort", "escorts", "explicit content", "sex"],
            Weak: ["sexy", "lingerie", "hot singles", "adults only", "18+", "seductive"]),
        new(RiskCategory.Hate,
            Strong: ["subhuman", "inferior race", "racial purity", "go back to your country", "hate group", "ethnic cleansing"],
            Weak: ["those people", "not welcome here", "invaders", "vermin"]),
        new(RiskCategory.Weapons,
            Strong: ["gun", "guns", "handgun", "rifle", "rifles", "pistol", "pistols", "firearm", "firearms", "ammo", "ammunition", "grenade", "assault weapon", "silencer"],
            Weak: ["knife", "knives", "blade", "crossbow", "tactical", "taser"]),
        new(RiskCategory.Alcohol,
            Strong: ["beer", "beers", "vodka", "whisky", "whiskey", "wine", "wines", "rum", "gin", "tequila", "cocktail", "cocktails", "liquor", "booze", "lager", "champagne", "cider"],
            Weak: ["drinks", "happy hour", "pub", "brewery", "cheers", "on tap"]),
        new(RiskCategory.Gambling,
            Strong: ["casino", "casinos", "betting", "bet", "bets", "poker", "jackpot", "slots", "slot machine", "roulette", "sportsbook", "wager", "free spins", "blackjack"],
            Weak: ["odds", "lottery", "win big", "lucky", "scratch card"]),
        new(RiskCategory.MisleadingClaims,
            Strong: ["guaranteed results", "guaranteed income", "miracle", "cure cancer", "cures cancer", "100% cure", "get rich quick", "risk-free", "risk free", "doctors hate", "no side effects", "double your money", "lose weight fast", "instant results"],
            Weak: ["guaranteed", "clinically proven", "best in the world", "secret trick", "100% effective"]),
    ];

    public string Name => "fake";

    public Task<AnalyzerResult> AnalyzeAsync(AnalysisInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var result = input.Kind == InputKind.Text
            ? AnalyzeText(input.Text!)
            : new AnalyzerResult([], 0.0, "The fake analyzer cannot look at images, so a person must review this image.");

        return Task.FromResult(result);
    }

    /// <summary>Pure function, exposed for tests.</summary>
    public static AnalyzerResult AnalyzeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var strongHits = new List<(RiskCategory Category, string Term)>();
        var weakHits = new List<(RiskCategory Category, string Term)>();

        foreach (var rule in Rules)
        {
            strongHits.AddRange(rule.StrongPatterns.Where(p => p.Regex.IsMatch(text)).Select(p => (rule.Category, p.Term)));
            weakHits.AddRange(rule.WeakPatterns.Where(p => p.Regex.IsMatch(text)).Select(p => (rule.Category, p.Term)));
        }

        if (strongHits.Count == 0 && weakHits.Count == 0)
        {
            return AnalyzerResult.Clean(NoMatchConfidence, "No risky keywords found (keyword rules only).");
        }

        var allHits = strongHits.Concat(weakHits).ToList();
        var categories = allHits.Select(h => h.Category).Distinct().Order().ToArray();
        var confidence = strongHits.Count > 0 ? StrongMatchConfidence : WeakMatchConfidence;
        var strength = strongHits.Count > 0 ? "strong" : "only weak";
        var reason = $"Matched {strength} keyword(s): {Describe(allHits)}.";

        return new AnalyzerResult(categories, confidence, reason);
    }

    private static string Describe(IEnumerable<(RiskCategory Category, string Term)> hits) =>
        string.Join("; ", hits
            .GroupBy(h => h.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key.ToWireName()} ({string.Join(", ", g.Select(h => $"'{h.Term}'").Distinct())})"));

    private sealed record Rule(RiskCategory Category, string[] Strong, string[] Weak)
    {
        public IReadOnlyList<TermPattern> StrongPatterns { get; } = Strong.Select(TermPattern.Create).ToArray();

        public IReadOnlyList<TermPattern> WeakPatterns { get; } = Weak.Select(TermPattern.Create).ToArray();
    }

    private sealed record TermPattern(string Term, Regex Regex)
    {
        // Whole-word match: "kill" matches "kill" but not "killer" or "skill".
        public static TermPattern Create(string term) => new(
            term,
            new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)));
    }
}
