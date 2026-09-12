using NawiriAI.Abstractions;
using NawiriAI.Providers.Common;

namespace NawiriAI.Providers.HuggingFace;

public sealed class HuggingFaceProvider(HttpClient httpClient, ISecretStore secretStore)
    : ChatCompletionProvider(httpClient, secretStore)
{
    public override string Id => "huggingface";
    public override string DisplayName => "Hugging Face";
    protected override Uri DefaultEndpoint { get; } = new("https://router.huggingface.co/v1/chat/completions");
}
