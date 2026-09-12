# Provider adapters

All adapters implement `IAIProvider` from `NawiriAI.Abstractions`. They receive an injected `HttpClient` and `ISecretStore`, use one HTTP request per operation, and return normalized text and optional token counts. They never retry automatically or switch providers. They contain no provider credentials and make no calls until invoked by a host.

| Provider ID | Class / package | Default complete inference endpoint | Protocol |
| --- | --- | --- | --- |
| `gemini` | `GeminiProvider` / `NawiriAI.Providers.Gemini` | `https://generativelanguage.googleapis.com/v1beta/openai/chat/completions` | OpenAI-compatible Chat Completions |
| `openai` | `OpenAIProvider` / `NawiriAI.Providers.OpenAI` | `https://api.openai.com/v1/responses` | OpenAI Responses |
| `huggingface` | `HuggingFaceProvider` / `NawiriAI.Providers.HuggingFace` | `https://router.huggingface.co/v1/chat/completions` | Hugging Face Inference Providers router |
| `local` | `OpenAICompatibleProvider` / `NawiriAI.Providers.OpenAI` | `http://localhost:11434/v1/chat/completions` | Local OpenAI-compatible Chat Completions |

These are protocol choices, not guarantees of account access or model availability. Model IDs are always host configuration; the adapters do not replace or silently upgrade a configured model. See [Gemini](gemini.md), [OpenAI](openai.md), [Hugging Face](huggingface.md), and [local servers](local.md).

## Host setup

Create and reuse the HTTP clients at the host boundary. Disable automatic redirects, cookies, and request body/header logging. Redirects must be disabled **before** requests are sent because a library receiving an existing `HttpClient` cannot change its underlying handler. The adapter rejects redirect status codes and detects a changed final response URI, but that final check cannot undo an HTTP redirect already followed by the host's handler.

```csharp
using NawiriAI.Abstractions;
using NawiriAI.Providers.Gemini;

using var handler = new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
};
using var httpClient = new HttpClient(handler)
{
    Timeout = Timeout.InfiniteTimeSpan
};

// Implement ISecretStore with the host's OS vault or another approved secret store.
// The adapter does not own/dispose httpClient or secretStore.
IAIProvider provider = new GeminiProvider(httpClient, secretStore);
var configuration = new AIProviderConfiguration(
    ProviderId: "gemini",
    ModelId: selectedModelId,
    SecretReference: selectedVaultReference,
    DataSharingApproved: sharingApprovedForThisProvider);

var result = await provider.CompleteAsync(
    new AIRequest([new AIMessage("user", "Summarize this approved aggregate: ...")]),
    configuration,
    cancellationToken);
```

The host owns `secretStore`, `selectedModelId`, `selectedVaultReference`, `sharingApprovedForThisProvider`, and `cancellationToken` in this example. A secret reference is a lookup identifier, never a raw key. Secrets are fetched afresh for each request and attached only to that request's `Authorization: Bearer` header. Do not put credentials in `DefaultRequestHeaders`, URLs, model IDs, settings files, telemetry, or logs. Credential lookup exceptions and remote HTTP error bodies are replaced with fixed safe messages; inner exceptions are not exposed.

`Endpoint`, when provided, is the **complete inference URL**, not a base URL. It replaces the default exactly. HTTPS is required except for the `local` adapter's loopback HTTP endpoint. URL user information, query strings, and fragments are rejected. Cloud providers and non-loopback local endpoints require both `DataSharingApproved = true` and a secret reference. Approval should be tied to the selected endpoint in the host's settings flow; a host must not carry an old approval across unrelated endpoint changes.

Keep all tenant scoping, data minimization, approval UI, and business rules in the host/core layer. The provider receives already-approved message text and cannot infer which customer or data fields were permitted.

## Requests and limits

- Text conversation roles: `system`, `developer`, `user`, `assistant`. Tools and streaming are not implemented; both capabilities are advertised as false.
- Between 1 and 128 nonempty messages, with at most 131,072 total message characters.
- An explicit, nonempty model ID of at most 256 characters.
- `MaxOutputTokens`: 1–32,768 (default 600). The provider or model may impose a smaller limit or spend part of this budget on reasoning.
- `TimeoutSeconds`: 1–300 (default 30). The linked deadline covers secret retrieval, HTTP headers, and response body reads. A shorter host `HttpClient.Timeout` also applies. Secret stores and custom handlers must honor cancellation.
- Response body limit: 1 MiB, enforced both with and without `Content-Length`.

Caller cancellation remains `OperationCanceledException`; timeouts and transport/provider failures become `AIProviderException` with safe fixed messages. HTTP 401, 403, 429, timeouts, redirects, rejected requests, and server failures have separate messages. No remote error body, credential, or endpoint is added to these errors.

## Structured responses

Adapters can transmit JSON schemas, so their adapter-level `Capabilities.StructuredOutput` is true. Set `AIProviderConfiguration.SupportsStructuredOutput` for the **selected model/server** only when its provider documentation and deployment support it. A request with `JsonSchema` is rejected if that setting is false. There is no automatic downgrade or second paid request.

`AIRequest.JsonSchema` must be a JSON object no larger than 65,536 characters. The adapters send strict JSON schema configuration in the appropriate protocol shape. They check that generated structured text is valid JSON with an object root. This is not a general JSON Schema validator: the core/host must still deserialize and validate its expected result contract, allowlisted fields, business constraints, and values before using the result.

Successful plain-text responses are nonempty. Refusals and truncated completions are reported as failures, not partially accepted business results. Reported token counts are optional and use `null` for missing or invalid values; the adapters never fabricate token estimates or totals. `AIResponse.ModelId` identifies the configured model/alias, not a verified server revision.

## Connection tests

`TestConnectionAsync` sends a fixed `Reply with OK.` prompt to the configured model, with at most 64 output tokens and the same endpoint/authentication/timeout checks. It consumes inference quota and can incur charges. It should run only when the user explicitly invokes the host's connection test action.

A recognized output-token-limit response also counts as successful connectivity, since a reasoning model can spend a small probe budget before producing text. Ordinary `CompleteAsync` calls continue to reject such incomplete output. Authentication errors, malformed output, provider refusals, and other failures return a safe `AIConnectionResult(false, status)`; caller cancellation still propagates.

## Verification

```powershell
dotnet test tests/NawiriAI.Provider.Tests/NawiriAI.Provider.Tests.csproj
```

The tests use in-memory handlers and synthetic secrets only. They do not contact Google, OpenAI, Hugging Face, or a local inference server. They verify wire formats, headers and secret rotation, parsing, schema transmission, failed responses, configuration gates, both response-size paths, deadlines, cancellation, and connection probes. An actual account/model smoke test is a separate host action requiring configured credentials and consent.
