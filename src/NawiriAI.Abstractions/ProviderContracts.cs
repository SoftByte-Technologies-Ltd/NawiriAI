namespace NawiriAI.Abstractions;

public sealed record AIProviderCapabilities(bool StructuredOutput, bool Tools, bool Streaming);
public sealed record AIModelDescriptor(string ProviderId, string ModelId, string DisplayName,
    AIProviderCapabilities Capabilities, bool Enabled = true, bool Recommended = false);
/// <summary>SecretReference identifies a host-owned vault entry. It is never a raw key.</summary>
public sealed record AIProviderConfiguration(string ProviderId, string ModelId,
    string? SecretReference = null, Uri? Endpoint = null, int TimeoutSeconds = 30,
    int MaxOutputTokens = 600, bool Enabled = true, bool DataSharingApproved = false,
    bool SupportsStructuredOutput = false);
public sealed record AIMessage(string Role, string Content);
public sealed record AIRequest(IReadOnlyList<AIMessage> Messages, string? JsonSchema = null);
public sealed record AIUsage(int? InputTokens = null, int? OutputTokens = null, int? TotalTokens = null);
public sealed record AIResponse(string Text, string ProviderId, string ModelId, AIUsage? Usage = null);
public sealed record AIConnectionResult(bool Success, string Status);

public interface ISecretStore
{
    ValueTask<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default);
}
public interface IAIProvider
{
    string Id { get; }
    string DisplayName { get; }
    AIProviderCapabilities Capabilities { get; }
    Task<AIResponse> CompleteAsync(AIRequest request, AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default);
    Task<AIConnectionResult> TestConnectionAsync(AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default);
}
public interface IAIProviderRouter
{
    IReadOnlyList<IAIProvider> Providers { get; }
    Task<AIResponse> CompleteAsync(AIRequest request, AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default);
}
/// <summary>Messages are deliberately generic; never include remote response bodies or credentials.</summary>
public sealed class AIProviderException(string message) : Exception(message);
