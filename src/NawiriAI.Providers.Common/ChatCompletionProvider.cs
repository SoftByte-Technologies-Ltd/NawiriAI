using System.Text;
using System.Text.Json;
using NawiriAI.Abstractions;

namespace NawiriAI.Providers.Common;

/// <summary>Shared wire format used by Gemini, Hugging Face and local compatible servers.</summary>
public abstract class ChatCompletionProvider(HttpClient httpClient, ISecretStore secretStore) : HttpAIProvider(httpClient, secretStore)
{
    protected override bool IsOutputLimitResponse(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return false;
        var choice = choices[0];
        return ReadString(choice, "finish_reason") == "length" && choice.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object;
    }

    protected override object CreatePayload(AIRequest request, AIProviderConfiguration configuration, JsonElement? schema)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = configuration.ModelId,
            ["messages"] = request.Messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            ["max_tokens"] = configuration.MaxOutputTokens,
            ["stream"] = false
        };
        if (schema is not null)
            payload["response_format"] = new
            {
                type = "json_schema",
                json_schema = new { name = "nawiri_response", schema = schema.Value, strict = true }
            };
        return payload;
    }

    protected override AIResponse ParseResponse(JsonElement root, AIProviderConfiguration configuration)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw InvalidResponse();
        var choice = choices[0];
        var finishReason = ReadString(choice, "finish_reason");
        if (finishReason is "length") throw new AIProviderException("The provider response was incomplete because the output limit was reached.");
        if (finishReason is "content_filter") throw new AIProviderException("The provider declined to return a response.");
        if (finishReason is not (null or "stop")) throw InvalidResponse();
        if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            throw InvalidResponse();
        if (!string.IsNullOrEmpty(ReadString(message, "refusal")))
            throw new AIProviderException("The provider declined to return a response.");
        if (!message.TryGetProperty("content", out var content)) throw InvalidResponse();
        var text = new StringBuilder();
        if (content.ValueKind == JsonValueKind.String) text.Append(content.GetString());
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (ReadString(part, "type") is "refusal")
                    throw new AIProviderException("The provider declined to return a response.");
                if (ReadString(part, "type") is "text" or "output_text") text.Append(ReadString(part, "text"));
            }
        }
        return new(text.ToString(), Id, configuration.ModelId, ReadUsage(root, "prompt_tokens", "completion_tokens"));
    }
}
