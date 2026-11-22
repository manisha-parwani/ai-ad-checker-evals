using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AdChecker.Core;

/// <summary>Shared JSON settings so the API, the eval runner and the test set all use the same wire names.</summary>
public static class JsonDefaults
{
    /// <summary>Enums as snake_case strings ("needs_review", "misleading_claims"). Numbers are not accepted.</summary>
    public static JsonStringEnumConverter EnumConverter() =>
        new(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(EnumConverter());
        options.MakeReadOnly();
        return options;
    }
}
