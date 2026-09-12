using System.Text;
using System.Text.Json;
using NawiriAI.Abstractions;
using NawiriAI.Providers.Common;

namespace NawiriAI.Providers.OpenAI;

public sealed class OpenAIProvider(HttpClient httpClient, ISecretStore secretStore)
    : HttpAIProvider(httpClient, secretStore)
{
    public override string Id => "openai";
    public override string DisplayName => "OpenAI";
    protected override Uri DefaultEndpoint { get; } = new("https://api.openai.com/v1/responses");

    protected override bool IsOutputLimitResponse(JsonElement root) => ReadString(root, "status") == "incomplete"
        && root.TryGetProperty("incomplete_details", out var details) && ReadString(details, "reason") == "max_output_tokens"
        && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array;

    protected override object CreatePayload(AIRequest request, AIProviderConfiguration configuration, JsonElement? schema)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = configuration.ModelId,
            ["input"] = request.Messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            ["max_output_tokens"] = configuration.MaxOutputTokens,
            ["stream"] = false,
            ["store"] = false
        };
        if (schema is not null)
            payload["text"] = new { format = new { type = "json_schema", name = "nawiri_response", schema = schema.Value, strict = true } };
        return payload;
    }

    protected override AIResponse ParseResponse(JsonElement root, AIProviderConfiguration configuration)
    {
        var status = ReadString(root, "status");
        if (status is "incomplete") throw new AIProviderException("The provider response was incomplete. Check the output token limit.");
        if (status is not (null or "completed")) throw new AIProviderException("The provider did not complete the response.");
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        var text = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (ReadString(item, "type") != "message") continue;
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) throw InvalidResponse();
            foreach (var part in content.EnumerateArray())
            {
                if (ReadString(part, "type") is "refusal") throw new AIProviderException("The provider declined to return a response.");
                if (ReadString(part, "type") is "output_text") text.Append(ReadString(part, "text"));
            }
        }
        return new(text.ToString(), Id, configuration.ModelId, ReadUsage(root, "input_tokens", "output_tokens"));
    }
}
