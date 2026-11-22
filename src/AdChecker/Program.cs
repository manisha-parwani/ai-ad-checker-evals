using AdChecker;
using AdChecker.Core;
using AdChecker.Core.Analysis;
using AdChecker.Core.Analysis.ContentUnderstanding;
using AdChecker.Core.Decisions;
using AdChecker.Core.Guardrails;
using Azure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Guardrail: small request bodies only. Ad text is short; this stops huge payloads early.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 64 * 1024);

builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(JsonDefaults.EnumConverter()));

builder.Services.AddOptions<AdCheckerSettings>().BindConfiguration(AdCheckerSettings.SectionName);
builder.Services.AddOptions<ContentUnderstandingOptions>().BindConfiguration("ContentUnderstanding");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHostResolver, DnsHostResolver>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AdCheckerSettings>>().Value.Guardrails);
builder.Services.AddSingleton<ImageUrlGuard>();
builder.Services.AddSingleton<CheckRequestValidator>();
builder.Services.AddSingleton(sp => new DecisionPolicy(sp.GetRequiredService<IOptions<AdCheckerSettings>>().Value.ReviewConfidenceThreshold));
builder.Services.AddSingleton<IContentAnalyzer>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AdCheckerSettings>>().Value;
    return settings.Analyzer switch
    {
        AnalyzerKind.ContentUnderstanding => new ContentUnderstandingAnalyzer(
            ContentUnderstandingAnalyzer.CreateHttpClient(),
            new DefaultAzureCredential(), // az login locally, managed identity in Azure. No keys.
            sp.GetRequiredService<IOptions<ContentUnderstandingOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()),
        _ => new FakeContentAnalyzer(),
    };
});
builder.Services.AddSingleton<AdCheckService>();

var app = builder.Build();

// Malformed JSON is the caller's fault (400), everything else unexpected is ours (500). Both return problem details.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

// Fail fast on bad configuration (missing endpoint, bad allow-list entry, bad threshold).
var analyzer = app.Services.GetRequiredService<IContentAnalyzer>();
app.Services.GetRequiredService<CheckRequestValidator>();
app.Services.GetRequiredService<DecisionPolicy>();
app.Logger.LogInformation("Ad checker started with the '{Analyzer}' analyzer", analyzer.Name);

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok", analyzer = analyzer.Name }));

app.MapPost("/check", async Task<Results<Ok<CheckResponse>, ValidationProblem, ProblemHttpResult>> (
    CheckRequest? request,
    CheckRequestValidator validator,
    AdCheckService service,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("AdChecker.Check");
    var validation = await validator.ValidateAsync(request, cancellationToken);
    if (!validation.IsValid)
    {
        return TypedResults.ValidationProblem(validation.Errors.ToDictionary());
    }

    var input = validation.Request!;
    try
    {
        var result = await service.CheckAsync(input.Text, input.ImageUrl, cancellationToken);

        // Log the outcome, not the ad content.
        logger.LogInformation(
            "Checked ad (text length {TextLength}, image host {ImageHost}): {Decision} {Categories} confidence {Confidence}",
            input.Text?.Length ?? 0, input.ImageUrl?.IdnHost ?? "-", result.Decision, result.Categories, result.Confidence);

        return TypedResults.Ok(CheckResponse.From(result, service.AnalyzerName));
    }
    catch (AnalyzerException ex)
    {
        logger.LogError(ex, "Analyzer failed");
        return TypedResults.Problem(
            title: "Analyzer unavailable",
            detail: "The ad was not checked, so nothing was approved. Try again later.",
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
