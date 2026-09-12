using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using NawiriAI.Abstractions;
using NawiriAI.Api;
using NawiriAI.Connectors;
using NawiriAI.Core;
using NawiriAI.Providers.Gemini;
using NawiriAI.Providers.HuggingFace;
using NawiriAI.Providers.OpenAI;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
if (!bool.TryParse(builder.Configuration["NAWIRIAI_DEMO_MODE"], out var demoMode) || !demoMode)
    throw new InvalidOperationException("This host uses synthetic data. Set NAWIRIAI_DEMO_MODE=true or supply a production host integration.");
if (builder.Configuration["NAWIRIAI_DEMO_TOKEN"] is not { Length: >= 32 })
    throw new InvalidOperationException("Set NAWIRIAI_DEMO_TOKEN to a random secret of at least 32 characters.");

var zone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["NAWIRIAI_TIMEZONE"] ?? "Africa/Nairobi");
var configuredDate = builder.Configuration["NAWIRIAI_DEMO_DATE"];
var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
if (!string.IsNullOrEmpty(configuredDate)
    && !DateOnly.TryParseExact(configuredDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
    throw new InvalidOperationException("NAWIRIAI_DEMO_DATE must be a valid calendar date in yyyy-MM-dd format.");
var localNoon = today.ToDateTime(new TimeOnly(12, 0));
if (zone.IsInvalidTime(localNoon))
    throw new InvalidOperationException("NAWIRIAI_DEMO_DATE does not exist in the configured business time zone.");
var clockUtc = TimeZoneInfo.ConvertTimeToUtc(localNoon, zone);
builder.Services.AddSingleton<TimeProvider>(new DemoClock(new DateTimeOffset(clockUtc)));
builder.Services.AddSingleton(zone);
builder.Services.AddSingleton<IBusinessDataProvider>(new SyntheticBusinessDataProvider(today));
builder.Services.AddSingleton<IBusinessSecurityContextAccessor, DemoSecurityContextAccessor>();
builder.Services.AddSingleton<ISecretStore>(new EnvironmentSecretStore(new Dictionary<string, string> { ["demo-provider-key"] = "NAWIRIAI_API_KEY" }));
builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan });
builder.Services.AddSingleton<IAIProvider, GeminiProvider>();
builder.Services.AddSingleton<IAIProvider, OpenAIProvider>();
builder.Services.AddSingleton<IAIProvider, HuggingFaceProvider>();
builder.Services.AddSingleton<IAIProvider, OpenAICompatibleProvider>();
builder.Services.AddSingleton<IAIProviderRouter, ProviderRouter>();
builder.Services.AddSingleton<IAIAuditSink, LoggingAuditSink>();
builder.Services.AddSingleton<IntelligenceService>();
builder.Services.AddAuthentication("demo").AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("demo", _ => { });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<BusinessMetric>(allowIntegerValues: false)));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("business", context => RateLimitPartition.GetFixedWindowLimiter(context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 499; }
    catch (Exception exception)
    {
        var (status, title) = exception switch
        {
            BusinessAccessException => (403, "Business data access denied."),
            BusinessQueryException => (400, "Invalid or unsupported business query."),
            BusinessCapabilityException => (422, "This information is not available from the connected business system."),
            BadHttpRequestException => (400, "Invalid request body."),
            AIProviderException => (503, "The configured AI provider is unavailable."),
            _ => (500, "The business request could not be completed.")
        };
        // Do not return/log raw exception messages or provider HTTP bodies.
        app.Logger.LogWarning("Request {CorrelationId} rejected with status {Status} category {Category}", context.TraceIdentifier, status, exception.GetType().Name);
        if (!context.Response.HasStarted)
            await Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", mode = "synthetic-demo", version = "0.1.0" }));
var api = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("business");
api.MapGet("/providers", (IAIProviderRouter router, IBusinessDataProvider data) => Results.Ok(new
{
    providers = router.Providers.Select(provider => new { provider.Id, provider.DisplayName, provider.Capabilities }),
    models = DemoProviderConfiguration.Read(app.Configuration) is { } configured
        ? new[] { new { configured.ProviderId, configured.ModelId } } : [],
    tools = ToolRegistry.Tools.Select(tool => new { tool.Metric, tool.Permission, supported = data.SupportedMetrics.Contains(tool.Metric) })
}));
api.MapPost("/chat", async (JsonElement body, HttpContext http, IBusinessSecurityContextAccessor contexts, IntelligenceService service) =>
    Results.Ok(await service.AskAsync(ChatRequestParser.ReadQuestion(body), contexts.GetContext(http), DemoProviderConfiguration.Read(app.Configuration), http.RequestAborted)));
api.MapPost("/query", async (JsonElement body, HttpContext http, IBusinessSecurityContextAccessor contexts, IntelligenceService service) =>
    Results.Ok(await service.QueryAsync(QueryPolicy.ParseJson(body.GetRawText()), contexts.GetContext(http), DemoProviderConfiguration.Read(app.Configuration), http.RequestAborted)));
api.MapGet("/insights/daily", async (HttpContext http, IBusinessSecurityContextAccessor contexts, IntelligenceService service) =>
    Results.Ok(await service.QueryAsync(new(BusinessMetric.DailyBrief, new(today.AddDays(-1), today.AddDays(-1))), contexts.GetContext(http), DemoProviderConfiguration.Read(app.Configuration), http.RequestAborted)));
api.MapGet("/business-health", async (HttpContext http, IBusinessSecurityContextAccessor contexts, IntelligenceService service) =>
    Results.Ok(await service.QueryAsync(new(BusinessMetric.BusinessHealth, new(new(today.Year, today.Month, 1), today)), contexts.GetContext(http), DemoProviderConfiguration.Read(app.Configuration), http.RequestAborted)));
app.Run();

public sealed class DemoClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
public partial class Program;
