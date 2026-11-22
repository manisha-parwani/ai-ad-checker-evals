using System.Text.Json;
using AdChecker.Core.Analysis.ContentUnderstanding;
using AdChecker.Core.Model;

namespace AdChecker.Tests;

public class ContentUnderstandingResultParserTests
{
    private static AnalyzerResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ContentUnderstandingResultParser.Parse(document.RootElement);
    }

    [Fact]
    public void None_with_no_other_categories_is_clean()
    {
        var result = Parse(ContentUnderstandingAnalyzerTests.SucceededJson("none", 0.88, "Bakery ad."));

        Assert.Empty(result.Categories);
        Assert.Equal(0.88, result.Confidence);
    }

    [Fact]
    public void Other_categories_are_added_and_none_is_ignored()
    {
        var result = Parse(ContentUnderstandingAnalyzerTests.SucceededJson("none", 0.9, "x", "none", "weapons"));

        Assert.Equal([RiskCategory.Weapons], result.Categories);
    }

    [Fact]
    public void Unknown_category_removes_the_confidence_so_a_person_reviews_it()
    {
        var result = Parse(ContentUnderstandingAnalyzerTests.SucceededJson("drugs", 0.99, "Sells pills."));

        Assert.Null(result.Confidence);
        Assert.Contains("unknown category 'drugs'", result.Reason);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.2)]
    [InlineData(85)]
    public void Confidence_outside_zero_to_one_is_not_trusted(double score) =>
        Assert.Null(Parse(ContentUnderstandingAnalyzerTests.SucceededJson("alcohol", score, "Beer.")).Confidence);

    [Fact]
    public void Falls_back_to_the_service_field_confidence()
    {
        const string json = """
            {"status":"Succeeded","result":{"contents":[{"fields":{
              "PrimaryCategory":{"type":"string","valueString":"hate","confidence":0.77},
              "Reason":{"type":"string","valueString":"Dehumanising language."}}}]}}
            """;

        var result = Parse(json);

        Assert.Equal([RiskCategory.Hate], result.Categories);
        Assert.Equal(0.77, result.Confidence);
    }

    [Fact]
    public void Missing_confidence_is_null()
    {
        var result = Parse(ContentUnderstandingAnalyzerTests.SucceededJson("gambling", null, "Casino."));

        Assert.Equal([RiskCategory.Gambling], result.Categories);
        Assert.Null(result.Confidence);
    }

    [Theory]
    [InlineData("""{"status":"Succeeded"}""")]
    [InlineData("""{"status":"Succeeded","result":{"contents":[]}}""")]
    [InlineData("""{"status":"Succeeded","result":{"contents":[{"markdown":"no fields"}]}}""")]
    public void No_fields_means_no_confidence(string json)
    {
        var result = Parse(json);

        Assert.Empty(result.Categories);
        Assert.Null(result.Confidence);
    }
}
