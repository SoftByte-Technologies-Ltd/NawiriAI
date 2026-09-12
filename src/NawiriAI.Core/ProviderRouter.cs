using NawiriAI.Abstractions;

namespace NawiriAI.Core;

public sealed class ProviderRouter(IEnumerable<IAIProvider> providers) : IAIProviderRouter
{
    public IReadOnlyList<IAIProvider> Providers { get; } = Array.AsReadOnly(providers.ToArray());
    public Task<AIResponse> CompleteAsync(AIRequest request, AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!configuration.Enabled || !configuration.DataSharingApproved)
            throw new AIProviderException("The selected provider is disabled or data sharing has not been approved.");
        var provider = Providers.SingleOrDefault(p => p.Id == configuration.ProviderId)
            ?? throw new AIProviderException("The configured provider is not registered.");
        return provider.CompleteAsync(request, configuration, cancellationToken);
    }
}
