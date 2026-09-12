using System.Text.Json;
using NawiriAI.Abstractions;

namespace NawiriAI.Api;

public static class ChatRequestParser
{
    public static string ReadQuestion(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) throw InvalidRequest();
        string? question = null;
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in body.EnumerateObject())
        {
            if (!fields.Add(property.Name) || !string.Equals(property.Name, "question", StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String)
                throw InvalidRequest();
            question = property.Value.GetString();
        }
        if (string.IsNullOrWhiteSpace(question) || question.Length > 2000) throw InvalidRequest();
        return question;
    }

    private static BusinessQueryException InvalidRequest() => new("Provide exactly one question string of at most 2000 characters; additional or duplicate fields are not accepted.");
}
