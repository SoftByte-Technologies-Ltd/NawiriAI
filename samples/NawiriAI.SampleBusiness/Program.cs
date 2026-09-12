using System.Globalization;
using NawiriAI.Abstractions;
using NawiriAI.Connectors;
using NawiriAI.Core;
using NawiriAI.Providers.Gemini;
using NawiriAI.Providers.HuggingFace;
using NawiriAI.Providers.OpenAI;

try
{
    if (args.Contains("--help"))
    {
        Console.WriteLine("NawiriAI synthetic business demo\nUsage: dotnet run --project samples/NawiriAI.SampleBusiness -- [--date YYYY-MM-DD] [business question]\nWithout a question, the three core examples run. The default uses no model and makes no network calls.\nOptional environment: NAWIRIAI_PROVIDER, NAWIRIAI_MODEL, NAWIRIAI_API_KEY, NAWIRIAI_DATA_SHARING_APPROVED, NAWIRIAI_ENDPOINT. See docs/examples/console-demo.md.");
        return 0;
    }
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var words = new List<string>();
    for (var index = 0; index < args.Length; index++)
    {
        if (args[index] == "--date")
        {
            if (++index >= args.Length || !DateOnly.TryParseExact(args[index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out today))
                throw new BusinessQueryException("The --date option requires YYYY-MM-DD.");
        }
        else if (args[index].StartsWith("--", StringComparison.Ordinal))
            throw new BusinessQueryException("Unknown option. Use --help for supported options.");
        else words.Add(args[index]);
    }
    if (today.DayNumber < 119) throw new BusinessQueryException("The demo date must allow the preceding 119 calendar days.");

    var providerId = Environment.GetEnvironmentVariable("NAWIRIAI_PROVIDER")?.Trim().ToLowerInvariant() ?? "none";
    var secretStore = new EnvironmentSecretStore(new Dictionary<string, string> { ["demo-provider-key"] = "NAWIRIAI_API_KEY" });
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    IAIProvider[] providers = [new GeminiProvider(http, secretStore), new OpenAIProvider(http, secretStore), new HuggingFaceProvider(http, secretStore), new OpenAICompatibleProvider(http, secretStore)];
    AIProviderConfiguration? configuration = null;
    if (providerId != "none")
    {
        if (!providers.Any(p => p.Id == providerId)) throw new BusinessQueryException("Select none, gemini, openai, huggingface, or local as the provider.");
        var model = Environment.GetEnvironmentVariable("NAWIRIAI_MODEL");
        if (string.IsNullOrWhiteSpace(model)) throw new BusinessQueryException("Set NAWIRIAI_MODEL to a model enabled for your selected provider.");
        var approved = bool.TryParse(Environment.GetEnvironmentVariable("NAWIRIAI_DATA_SHARING_APPROVED"), out var flag) && flag;
        Uri? endpoint = null;
        var endpointText = Environment.GetEnvironmentVariable("NAWIRIAI_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(endpointText) && !Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint))
            throw new BusinessQueryException("NAWIRIAI_ENDPOINT must be an absolute URI.");
        var secretReference = providerId == "local" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NAWIRIAI_API_KEY")) ? null : "demo-provider-key";
        configuration = new(providerId, model, secretReference, endpoint, DataSharingApproved: approved);
    }

    var data = new SyntheticBusinessDataProvider(today);
    var service = new IntelligenceService(data, new ProviderRouter(providers), clock: new DemoClock(today));
    var questions = words.Count > 0 ? new[] { string.Join(' ', words) }
        : new[] { "How much did we sell today?", "Show top 5 products this month", "Compare sales this month to last month" };
    Console.WriteLine($"NawiriAI synthetic demo | Today: {today:yyyy-MM-dd} | Fixture seed: 1729");
    foreach (var question in questions)
    {
        Console.WriteLine($"\n{question}");
        var answer = await service.AskAsync(question, SyntheticBusinessDataProvider.CreateDemoContext(Guid.NewGuid().ToString("N")), configuration);
        Console.WriteLine(answer.DisplayText);
        Console.WriteLine($"Provider status: {answer.ProviderStatus}");
        if (answer.Narrative is not null) Console.WriteLine("Qualitative model observation: " + answer.Narrative);
    }
    return 0;
}
catch (Exception error) when (error is BusinessQueryException or BusinessAccessException or BusinessCapabilityException or AIProviderException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

internal sealed class DemoClock(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new(12, 0)), TimeSpan.Zero);
}
