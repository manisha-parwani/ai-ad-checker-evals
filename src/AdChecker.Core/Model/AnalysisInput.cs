namespace AdChecker.Core.Model;

public enum InputKind
{
    Text,
    Image,
}

/// <summary>One thing to analyze: either ad text or an image URL that already passed the guardrails.</summary>
public sealed record AnalysisInput
{
    private AnalysisInput(InputKind kind, string? text, Uri? imageUrl)
    {
        Kind = kind;
        Text = text;
        ImageUrl = imageUrl;
    }

    public InputKind Kind { get; }

    public string? Text { get; }

    public Uri? ImageUrl { get; }

    public static AnalysisInput ForText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new AnalysisInput(InputKind.Text, text, null);
    }

    public static AnalysisInput ForImage(Uri imageUrl)
    {
        ArgumentNullException.ThrowIfNull(imageUrl);
        return new AnalysisInput(InputKind.Image, null, imageUrl);
    }
}
