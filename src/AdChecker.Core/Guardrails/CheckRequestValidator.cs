namespace AdChecker.Core.Guardrails;

/// <summary>Body of <c>POST /check</c>. Give text, an image URL, or both.</summary>
public sealed record CheckRequest(string? Text, string? ImageUrl);

/// <summary>A request that passed every guardrail. Only this type reaches the analyzer.</summary>
public sealed record ValidatedCheckRequest(string? Text, Uri? ImageUrl);

public sealed record ValidationOutcome(ValidatedCheckRequest? Request, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool IsValid => Request is not null && Errors.Count == 0;
}

public sealed class CheckRequestValidator(GuardrailOptions options, ImageUrlGuard imageUrlGuard)
{
    public async Task<ValidationOutcome> ValidateAsync(CheckRequest? request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                errors[key] = list = [];
            }

            list.Add(message);
        }

        if (request is null)
        {
            Add("request", "A JSON body is required.");
            return Fail(errors);
        }

        string? text = null;
        if (request.Text is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                Add("text", "text must not be blank. Leave it out if you only send an image.");
            }
            else if (request.Text.Length > options.MaxTextLength)
            {
                Add("text", $"text is longer than {options.MaxTextLength} characters.");
            }
            else if (request.Text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            {
                Add("text", "text must not contain control characters.");
            }
            else
            {
                text = request.Text.Trim();
            }
        }

        Uri? imageUrl = null;
        if (request.ImageUrl is not null)
        {
            var guard = await imageUrlGuard.CheckAsync(request.ImageUrl, cancellationToken).ConfigureAwait(false);
            if (guard.IsAllowed)
            {
                imageUrl = guard.Url;
            }
            else
            {
                Add("imageUrl", guard.Error!);
            }
        }

        if (request.Text is null && request.ImageUrl is null)
        {
            Add("request", "Give text, imageUrl, or both.");
        }

        return errors.Count > 0 ? Fail(errors) : new ValidationOutcome(new ValidatedCheckRequest(text, imageUrl), new Dictionary<string, string[]>());
    }

    private static ValidationOutcome Fail(Dictionary<string, List<string>> errors) =>
        new(null, errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}
