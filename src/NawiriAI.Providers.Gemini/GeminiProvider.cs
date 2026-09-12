using NawiriAI.Abstractions;
using NawiriAI.Providers.Common;

namespace NawiriAI.Providers.Gemini;

public sealed class GeminiProvider(HttpClient httpClient, ISecretStore secretStore)
    : ChatCompletionProvider(httpClient, secretStore)
{
    public override string Id => "gemini";
    public override string DisplayName => "Google Gemini";
    protected override Uri DefaultEndpoint { get; } = new("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions");
}
