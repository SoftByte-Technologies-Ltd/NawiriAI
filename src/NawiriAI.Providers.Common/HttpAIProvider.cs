using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NawiriAI.Abstractions;

namespace NawiriAI.Providers.Common;

public abstract class HttpAIProvider : IAIProvider
{
    private const int MaximumResponseBytes = 1_048_576;
    private const int MaximumInputCharacters = 131_072;
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;

    protected HttpAIProvider(HttpClient httpClient, ISecretStore secretStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public AIProviderCapabilities Capabilities { get; } = new(true, false, false);
    protected abstract Uri DefaultEndpoint { get; }
    protected virtual bool AllowUnauthenticatedLoopback => false;

    public Task<AIResponse> CompleteAsync(AIRequest request, AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
        => CompleteCoreAsync(request, configuration, false, cancellationToken);

    private async Task<AIResponse> CompleteCoreAsync(AIRequest request, AIProviderConfiguration configuration,
        bool connectionProbe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = Validate(request, configuration);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));

        try
        {
            JsonElement? schema = null;
            if (request.JsonSchema is not null)
            {
                using var schemaDocument = JsonDocument.Parse(request.JsonSchema);
                if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
                    throw new AIProviderException("The response schema must be a JSON object.");
                schema = schemaDocument.RootElement.Clone();
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(CreatePayload(request, configuration, schema)),
                    Encoding.UTF8, "application/json")
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var secret = await GetSecretAsync(configuration.SecretReference, deadline.Token).ConfigureAwait(false);
            if (secret is not null)
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw CreateHttpException(response.StatusCode);

            // Hosts must disable automatic redirects on the injected client's handler. This also
            // rejects an already-followed redirect, but cannot undo a host handler's earlier send.
            if (response.RequestMessage?.RequestUri is { } actualEndpoint && actualEndpoint != endpoint)
                throw new AIProviderException("The provider returned an unexpected redirect.");

            using var document = await ReadDocumentAsync(response, deadline.Token).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw InvalidResponse();
            if (connectionProbe && IsOutputLimitResponse(document.RootElement))
            {
                // A tiny probe may spend its budget on reasoning before producing text. The
                // recognized limit response still confirms authentication and model access.
                deadline.Token.ThrowIfCancellationRequested();
                return new("Connection probe accepted.", Id, configuration.ModelId);
            }
            var result = ParseResponse(document.RootElement, configuration);
            if (string.IsNullOrWhiteSpace(result.Text)) throw InvalidResponse();
            if (schema is not null)
            {
                using var structured = JsonDocument.Parse(result.Text);
                if (structured.RootElement.ValueKind != JsonValueKind.Object)
                    throw new AIProviderException("The provider did not return a JSON object.");
            }
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The provider request was cancelled.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new AIProviderException("The provider request timed out.");
        }
        catch (JsonException)
        {
            throw new AIProviderException("The provider request schema or response contains invalid JSON.");
        }
        catch (HttpRequestException)
        {
            throw new AIProviderException("The provider could not be reached. Check the connection and endpoint.");
        }
        catch (IOException)
        {
            throw new AIProviderException("The provider response could not be read.");
        }
    }

    /// <summary>Runs one small inference request. Cloud providers may charge for this request.</summary>
    public async Task<AIConnectionResult> TestConnectionAsync(AIProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            await CompleteCoreAsync(new AIRequest([new AIMessage("user", "Reply with OK.")]),
                configuration with { MaxOutputTokens = Math.Min(configuration.MaxOutputTokens, 64) }, true, cancellationToken)
                .ConfigureAwait(false);
            return new(true, "Connection succeeded; the configured model accepted the request.");
        }
        catch (AIProviderException exception)
        {
            return new(false, exception.Message);
        }
    }

    protected abstract object CreatePayload(AIRequest request, AIProviderConfiguration configuration, JsonElement? schema);
    protected abstract AIResponse ParseResponse(JsonElement root, AIProviderConfiguration configuration);
    protected abstract bool IsOutputLimitResponse(JsonElement root);

    protected static AIProviderException InvalidResponse() => new("The provider returned an empty or unsupported response.");

    protected static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    protected static AIUsage? ReadUsage(JsonElement root, string inputName, string outputName)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        return new(ReadTokenCount(usage, inputName), ReadTokenCount(usage, outputName), ReadTokenCount(usage, "total_tokens"));
    }

    private static int? ReadTokenCount(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0
            ? count : null;

    private Uri Validate(AIRequest request, AIProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!string.Equals(configuration.ProviderId, Id, StringComparison.Ordinal))
            throw new AIProviderException("The configuration does not match the selected provider.");
        if (!configuration.Enabled) throw new AIProviderException("The selected provider is disabled.");
        if (string.IsNullOrWhiteSpace(configuration.ModelId) || configuration.ModelId.Length > 256 || configuration.ModelId.Any(char.IsControl))
            throw new AIProviderException("A valid model identifier is required.");
        if (configuration.TimeoutSeconds is < 1 or > 300)
            throw new AIProviderException("The provider timeout must be between 1 and 300 seconds.");
        if (configuration.MaxOutputTokens is < 1 or > 32_768)
            throw new AIProviderException("The output token limit must be between 1 and 32768.");

        var endpoint = configuration.Endpoint ?? DefaultEndpoint;
        if (!endpoint.IsAbsoluteUri || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !(AllowUnauthenticatedLoopback && endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)))
            throw new AIProviderException("Use an HTTPS endpoint without URL credentials, query parameters or fragments; local loopback HTTP is also supported.");
        var local = AllowUnauthenticatedLoopback && endpoint.IsLoopback;
        if (!local && !configuration.DataSharingApproved)
            throw new AIProviderException("Data sharing approval is required for this provider endpoint.");
        if (!local && string.IsNullOrWhiteSpace(configuration.SecretReference))
            throw new AIProviderException("A credential reference is required for this provider.");
        if (request.Messages is null || request.Messages.Count is < 1 or > 128)
            throw new AIProviderException("Provide between 1 and 128 messages.");
        long characters = 0;
        foreach (var item in request.Messages)
        {
            if (item is null || item.Role is not ("system" or "developer" or "user" or "assistant") || string.IsNullOrWhiteSpace(item.Content))
                throw new AIProviderException("Messages must contain text and a supported conversation role.");
            characters += item.Content.Length;
        }
        if (characters > MaximumInputCharacters) throw new AIProviderException("The request exceeds the input size limit.");
        if (request.JsonSchema is not null)
        {
            if (!configuration.SupportsStructuredOutput)
                throw new AIProviderException("Structured output has not been enabled for this model.");
            if (request.JsonSchema.Length > 65_536) throw new AIProviderException("The response schema exceeds the size limit.");
        }
        return endpoint;
    }

    private async ValueTask<string?> GetSecretAsync(string? reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        string? secret;
        try
        {
            secret = await _secretStore.GetSecretAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Vault exceptions can contain paths, credential references or secret values.
            throw new AIProviderException("The provider credential could not be read from the secret store.");
        }
        if (string.IsNullOrWhiteSpace(secret)) throw new AIProviderException("The provider credential was not found in the secret store.");
        if (secret.Length > 16_384 || secret.Any(character => character is <= ' ' or > '~'))
            throw new AIProviderException("The provider credential has an invalid format.");
        return secret;
    }

    private static AIProviderException CreateHttpException(HttpStatusCode status) => new(status switch
    {
        HttpStatusCode.Unauthorized => "Provider authentication failed. Check the configured credential.",
        HttpStatusCode.Forbidden => "Access to the provider or model was denied.",
        HttpStatusCode.TooManyRequests => "The provider rate or quota limit was reached. Try again later.",
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "The provider request timed out.",
        >= HttpStatusCode.InternalServerError => "The provider is temporarily unavailable.",
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => "The provider returned an unexpected redirect.",
        _ => "The provider rejected the request. Check the selected model and configuration."
    });

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new AIProviderException("The provider response exceeded the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new AIProviderException("The provider response exceeded the size limit.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
