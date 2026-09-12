using System.Net;
using System.Text;
using System.Text.Json;
using NawiriAI.Abstractions;
using NawiriAI.Providers.Gemini;
using NawiriAI.Providers.HuggingFace;
using NawiriAI.Providers.OpenAI;
using Xunit;

namespace NawiriAI.Provider.Tests;

public sealed class ProviderTests
{
    private const string Schema = """
        {"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"],"additionalProperties":false}
        """;
    private const string ChatResponse = """
        {"choices":[{"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}
        """;
    private const string ResponsesResponse = """
        {"status":"completed","output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"Hel"},{"type":"output_text","text":"lo"}]}],"usage":{"input_tokens":12,"output_tokens":3,"total_tokens":15}}
        """;
    private static readonly AIRequest Request = new([new("system", "Be concise."), new("user", "Hello")]);

    [Theory]
    [InlineData("gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    [InlineData("openai", "https://api.openai.com/v1/responses")]
    [InlineData("huggingface", "https://router.huggingface.co/v1/chat/completions")]
    [InlineData("local", "http://localhost:11434/v1/chat/completions")]
    public async Task SendsConfiguredModelAndBoundedRequestAndParsesTextUsage(string id, string endpoint)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ResponseFor(id))));
        using var client = new HttpClient(handler);
        var secrets = new TestSecretStore("unit-test-key");
        var provider = CreateProvider(id, client, secrets);

        var response = await provider.CompleteAsync(Request, Configuration(id));

        Assert.Equal(endpoint, handler.Endpoint!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("Bearer unit-test-key", handler.Authorization);
        Assert.Equal("application/json", handler.MediaType);
        Assert.Equal("provider-test-reference", secrets.LastReference);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(600, body.RootElement.GetProperty(id == "openai" ? "max_output_tokens" : "max_tokens").GetInt32());
        var messages = body.RootElement.GetProperty(id == "openai" ? "input" : "messages");
        Assert.Equal("Be concise.", messages[0].GetProperty("content").GetString());
        Assert.Equal("Hello", messages[1].GetProperty("content").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        if (id == "openai") Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("Hello", response.Text);
        Assert.Equal(id, response.ProviderId);
        Assert.Equal("test-model", response.ModelId);
        Assert.Equal(new AIUsage(12, 3, 15), response.Usage);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("openai")]
    [InlineData("huggingface")]
    [InlineData("local")]
    public async Task SendsJsonSchemaInProviderSpecificFormat(string id)
    {
        var responseBody = id == "openai"
            ? """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"{\"answer\":\"yes\"}"}]}]}"""
            : """{"choices":[{"message":{"content":"{\"answer\":\"yes\"}"},"finish_reason":"stop"}]}""";
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(responseBody)));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(id, client);

        var response = await provider.CompleteAsync(Request with { JsonSchema = Schema }, Configuration(id));

        using var body = JsonDocument.Parse(handler.Body!);
        var format = id == "openai" ? body.RootElement.GetProperty("text").GetProperty("format")
            : body.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schemaFormat = id == "openai" ? format : format.GetProperty("json_schema");
        Assert.True(schemaFormat.GetProperty("strict").GetBoolean());
        Assert.Equal("object", schemaFormat.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("{\"answer\":\"yes\"}", response.Text);
    }

    [Fact]
    public async Task LocalEndpointWorksWithoutAnySecretLookup()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var secrets = new TestSecretStore(null);
        var provider = new OpenAICompatibleProvider(client, secrets);
        var configuration = Configuration("local") with
        {
            Endpoint = new Uri("http://127.0.0.1:1234/v1/chat/completions"),
            SecretReference = null,
            DataSharingApproved = false
        };

        await provider.CompleteAsync(Request, configuration);

        Assert.Equal(configuration.Endpoint, handler.Endpoint);
        Assert.Null(handler.Authorization);
        Assert.Equal(0, secrets.Calls);
    }

    [Fact]
    public async Task SecretIsRetrievedForEachRequestAndNeverStoredOnSharedClient()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var secrets = new TestSecretStore("first-key");
        var provider = new GeminiProvider(client, secrets);

        await provider.CompleteAsync(Request, Configuration("gemini"));
        Assert.Equal("Bearer first-key", handler.Authorization);
        secrets.Secret = "rotated-key";
        await provider.CompleteAsync(Request, Configuration("gemini"));

        Assert.Equal("Bearer rotated-key", handler.Authorization);
        Assert.Equal(2, secrets.Calls);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData(401, "authentication")]
    [InlineData(403, "access")]
    [InlineData(429, "rate")]
    [InlineData(500, "unavailable")]
    [InlineData(400, "rejected")]
    [InlineData(302, "redirect")]
    public async Task HttpErrorsAreSafeAndDoNotRetry(int statusCode, string expectedMessage)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)statusCode)
        { Content = new StringContent("unit-test-key confidential remote body") }));
        using var client = new HttpClient(handler);
        var provider = new GeminiProvider(client, new TestSecretStore("unit-test-key"));

        var exception = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, Configuration("gemini")));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unit-test-key", exception.ToString());
        Assert.DoesNotContain("confidential", exception.ToString());
        Assert.Null(exception.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task NetworkExceptionsCannotLeakCredentialsOrUrls()
    {
        using var handler = new RecordingHandler((_, _) => throw new HttpRequestException("secret-key at https://private.invalid"));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => new GeminiProvider(client, new TestSecretStore("secret-key"))
            .CompleteAsync(Request, Configuration("gemini")));
        Assert.DoesNotContain("secret-key", exception.ToString());
        Assert.DoesNotContain("private.invalid", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"refusal\":\"private refusal\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}")]
    public async Task RejectsMalformedEmptyRefusedAndIncompleteChatResponses(string body)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(body)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("gemini", client)
            .CompleteAsync(Request, Configuration("gemini")));
        Assert.DoesNotContain("private refusal", exception.ToString());
    }

    [Theory]
    [InlineData("{\"status\":\"incomplete\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"partial\"}]}]}")]
    [InlineData("{\"status\":\"failed\",\"error\":{\"message\":\"private failure\"}}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"private refusal\"}]}]}")]
    public async Task RejectsIncompleteFailedAndRefusedOpenAIResponses(string body)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(body)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("openai", client)
            .CompleteAsync(Request, Configuration("openai")));
        Assert.DoesNotContain("private", exception.ToString());
    }

    [Fact]
    public async Task MissingUsageRemainsUnknown()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""{"choices":[{"message":{"content":"Hello"}}]}""")));
        using var client = new HttpClient(handler);
        var response = await CreateProvider("local", client).CompleteAsync(Request, Configuration("local"));
        Assert.Null(response.Usage);
    }

    [Fact]
    public async Task DeadlineProducesSafeTimeout()
    {
        using var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ChatResponse);
        });
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("local", client)
            .CompleteAsync(Request, Configuration("local") with { TimeoutSeconds = 1 }));
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse(ChatResponse);
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProvider("local", client)
            .CompleteAsync(Request, Configuration("local"), cancellation.Token));
    }

    [Fact]
    public async Task TimeoutIncludesSecretRetrieval()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => new GeminiProvider(client, new SlowSecretStore())
            .CompleteAsync(Request, Configuration("gemini") with { TimeoutSeconds = 1 }));
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://remote.example/v1/chat/completions")]
    [InlineData("https://user:password@example.com/v1/chat/completions")]
    [InlineData("https://example.com/v1/chat/completions?api_key=secret")]
    [InlineData("https://example.com/v1/chat/completions#secret")]
    [InlineData("file:///C:/private")]
    public async Task UnsafeEndpointsAreRejectedBeforeSecretsOrNetwork(string endpoint)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var secrets = new TestSecretStore("unit-test-key");
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => new OpenAICompatibleProvider(client, secrets)
            .CompleteAsync(Request, Configuration("local") with { Endpoint = new Uri(endpoint) }));
        Assert.Equal(0, secrets.Calls);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain("password", exception.ToString());
        Assert.DoesNotContain("api_key", exception.ToString());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("wrong-provider")]
    [InlineData("no-consent")]
    [InlineData("no-model")]
    [InlineData("no-key")]
    [InlineData("no-schema-support")]
    [InlineData("invalid-schema")]
    [InlineData("invalid-timeout")]
    [InlineData("invalid-token-limit")]
    [InlineData("empty-messages")]
    [InlineData("invalid-role")]
    [InlineData("oversized-input")]
    public async Task InvalidConfigurationAndRequestsNeverSendHttp(string scenario)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var config = Configuration("gemini");
        var request = Request;
        switch (scenario)
        {
            case "disabled": config = config with { Enabled = false }; break;
            case "wrong-provider": config = config with { ProviderId = "other" }; break;
            case "no-consent": config = config with { DataSharingApproved = false }; break;
            case "no-model": config = config with { ModelId = " " }; break;
            case "no-key": config = config with { SecretReference = null }; break;
            case "no-schema-support": request = Request with { JsonSchema = Schema }; config = config with { SupportsStructuredOutput = false }; break;
            case "invalid-schema": request = Request with { JsonSchema = "invalid schema" }; break;
            case "invalid-timeout": config = config with { TimeoutSeconds = 0 }; break;
            case "invalid-token-limit": config = config with { MaxOutputTokens = 0 }; break;
            case "empty-messages": request = new([]); break;
            case "invalid-role": request = new([new("tool", "unsupported tool result")]); break;
            case "oversized-input": request = new([new("user", new string('a', 200_000))]); break;
        }

        await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("gemini", client).CompleteAsync(request, config));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ResponseBodyIsBounded()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(new string('x', 1_048_577))));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("local", client)
            .CompleteAsync(Request, Configuration("local")));
        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StructuredResponseMustBeJson()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("local", client)
            .CompleteAsync(Request with { JsonSchema = Schema }, Configuration("local")));
    }

    [Theory]
    [InlineData("gemini")]
    [InlineData("openai")]
    [InlineData("huggingface")]
    [InlineData("local")]
    public async Task TestConnectionChecksConfiguredModelWithSmallRequest(string id)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ResponseFor(id))));
        using var client = new HttpClient(handler);

        var result = await CreateProvider(id, client).TestConnectionAsync(Configuration(id));

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.InRange(body.RootElement.GetProperty(id == "openai" ? "max_output_tokens" : "max_tokens").GetInt32(), 1, 64);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TestConnectionReturnsSafeFailure()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("private-key") }));
        using var client = new HttpClient(handler);
        var result = await CreateProvider("openai", client).TestConnectionAsync(Configuration("openai"));
        Assert.False(result.Success);
        Assert.Contains("authentication", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-key", result.Status);
    }

    [Theory]
    [InlineData("gemini", "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":null}}]}")]
    [InlineData("openai", "{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"},\"output\":[]}")]
    public async Task TestConnectionAcceptsRecognizedProbeTokenLimit(string id, string body)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(body)));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(id, client);
        Assert.True((await provider.TestConnectionAsync(Configuration(id))).Success);
        await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request, Configuration(id)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("secret\r\nInjected: yes")]
    public async Task MissingOrInvalidVaultValueNeverSendsHttp(string? secret)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => new GeminiProvider(client, new TestSecretStore(secret))
            .CompleteAsync(Request, Configuration("gemini")));
        Assert.DoesNotContain("Injected", exception.ToString());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SecretStoreExceptionsAreSanitized()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => new GeminiProvider(client, new FailingSecretStore())
            .CompleteAsync(Request, Configuration("gemini")));
        Assert.DoesNotContain("vault-private", exception.ToString());
        Assert.Null(exception.InnerException);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task InvalidTokenUsageIsUnknownWithoutInventedTotals()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
            {"choices":[{"message":{"content":"Hello"}}],"usage":{"prompt_tokens":-1,"completion_tokens":3}}
            """)));
        using var client = new HttpClient(handler);
        var response = await CreateProvider("gemini", client).CompleteAsync(Request, Configuration("gemini"));
        Assert.Equal(new AIUsage(null, 3, null), response.Usage);
    }

    [Fact]
    public async Task ParsesChatTextContentParts()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
            {"choices":[{"message":{"content":[{"type":"text","text":"Hel"},{"type":"text","text":"lo"}]},"finish_reason":"stop"}]}
            """)));
        using var client = new HttpClient(handler);
        Assert.Equal("Hello", (await CreateProvider("local", client).CompleteAsync(Request, Configuration("local"))).Text);
    }

    [Fact]
    public async Task ChunkedResponseBodyIsAlsoBounded()
    {
        var bytesRead = 0;
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TestReadStream((buffer, _) =>
            {
                buffer.Span.Fill((byte)'x');
                bytesRead += buffer.Length;
                return ValueTask.FromResult(buffer.Length);
            }))
        }));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("local", client)
            .CompleteAsync(Request, Configuration("local")));
        Assert.Contains("limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(bytesRead, 1_048_577, 1_056_768);
    }

    [Fact]
    public async Task DeadlineIncludesResponseBodyRead()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new TestReadStream(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }))
        }));
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<AIProviderException>(() => CreateProvider("local", client)
            .CompleteAsync(Request, Configuration("local") with { TimeoutSeconds = 1 }));
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(ChatResponse)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateProvider("local", client)
            .TestConnectionAsync(Configuration("local"), cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private static AIProviderConfiguration Configuration(string id) => new(id, "test-model", "provider-test-reference",
        DataSharingApproved: true, SupportsStructuredOutput: true);

    private static IAIProvider CreateProvider(string id, HttpClient client, ISecretStore? secrets = null) => id switch
    {
        "gemini" => new GeminiProvider(client, secrets ?? new TestSecretStore("unit-test-key")),
        "openai" => new OpenAIProvider(client, secrets ?? new TestSecretStore("unit-test-key")),
        "huggingface" => new HuggingFaceProvider(client, secrets ?? new TestSecretStore("unit-test-key")),
        "local" => new OpenAICompatibleProvider(client, secrets ?? new TestSecretStore("unit-test-key")),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    private static string ResponseFor(string id) => id == "openai" ? ResponsesResponse : ChatResponse;
    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class TestSecretStore(string? secret) : ISecretStore
    {
        public string? Secret { get; set; } = secret;
        public int Calls { get; private set; }
        public string? LastReference { get; private set; }
        public ValueTask<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastReference = reference;
            return ValueTask.FromResult(Secret);
        }
    }

    private sealed class SlowSecretStore : ISecretStore
    {
        public async ValueTask<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class FailingSecretStore : ISecretStore
    {
        public ValueTask<string?> GetSecretAsync(string reference, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("vault-private credential contents");
    }

    private sealed class TestReadStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read(buffer, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Endpoint { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? MediaType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Endpoint = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }
}
