using NawiriAI.Abstractions;
using NawiriAI.Providers.Common;

namespace NawiriAI.Providers.OpenAI;

public sealed class OpenAICompatibleProvider(HttpClient httpClient, ISecretStore secretStore)
    : ChatCompletionProvider(httpClient, secretStore)
{
    public override string Id => "local";
    public override string DisplayName => "Local OpenAI-compatible";
    protected override Uri DefaultEndpoint { get; } = new("http://localhost:11434/v1/chat/completions");
    protected override bool AllowUnauthenticatedLoopback => true;
}
