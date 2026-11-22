using AdChecker.Core.Model;
using AdChecker.Evals;

namespace AdChecker.Tests;

/// <summary>Keeps the hand-written eval test set honest and well-formed.</summary>
public class TestSetTests
{
    private static readonly string TestSetPath = Path.Combine(AppContext.BaseDirectory, "testdata", "testset.json");

    [Fact]
    public async Task Test_set_loads_and_has_a_reasonable_shape()
    {
        var testSet = await TestSetLoader.LoadAsync(TestSetPath);

        Assert.InRange(testSet.Cases.Count, 20, 40);
        Assert.Contains(testSet.Cases, c => c.ExpectedDecision == Decision.Approved && c.MustPass);
        Assert.Contains(testSet.Cases, c => c.ExpectedDecision == Decision.NeedsReview);
        Assert.Contains(testSet.Cases, c => !c.MustPass);
    }

    [Fact]
    public async Task Every_risk_category_has_at_least_one_must_pass_case()
    {
        var testSet = await TestSetLoader.LoadAsync(TestSetPath);

        foreach (var category in RiskCategories.Risky)
        {
            Assert.Contains(testSet.Cases, c => c.ExpectedCategory == category && c.MustPass);
        }
    }

    [Fact]
    public void Validation_catches_duplicate_ids_and_inconsistent_expectations()
    {
        var bad = new TestSet(
        [
            new EvalCase("a", "text", Decision.Approved, RiskCategory.Gambling, true),
            new EvalCase("a", "text", Decision.Rejected, RiskCategory.None, true),
        ]);

        var ex = Assert.Throws<InvalidDataException>(() => TestSetLoader.Validate(bad));

        Assert.Contains("duplicate id 'a'", ex.Message);
        Assert.Contains("must be 'none'", ex.Message);
        Assert.Contains("needs an expectedCategory", ex.Message);
    }

    [Fact]
    public async Task Missing_must_pass_field_is_an_error()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{ "cases": [ { "id": "a", "text": "x", "expectedDecision": "approved", "expectedCategory": "none" } ] }""");

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => TestSetLoader.LoadAsync(path));

            Assert.Contains("mustPass", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
