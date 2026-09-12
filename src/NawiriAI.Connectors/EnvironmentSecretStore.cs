using NawiriAI.Abstractions;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace NawiriAI.Connectors;

public sealed class EnvironmentSecretStore : ISecretStore
{
    private readonly FrozenDictionary<string, string> references;
    /// <summary>The trusted host supplies a reference-to-environment-name allowlist. Callers cannot name arbitrary environment variables.</summary>
    public EnvironmentSecretStore(IReadOnlyDictionary<string, string> allowedReferences)
    {
        foreach (var pair in allowedReferences)
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || string.IsNullOrWhiteSpace(pair.Value)
                || pair.Value.Length > 128 || !Regex.IsMatch(pair.Value, "^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ArgumentException("Secret reference mappings require a name and a valid environment variable name.");
        references = allowedReferences.ToFrozenDictionary(StringComparer.Ordinal);
    }
    public ValueTask<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(references.TryGetValue(reference, out var name) ? Environment.GetEnvironmentVariable(name) : null);
    }
}
