# Google Gemini

`GeminiProvider` preserves the OpenAI-compatible Gemini endpoint:

```text
https://generativelanguage.googleapis.com/v1beta/openai/chat/completions
```

It sends a JSON POST containing the configured `model`, conversation `messages`, `max_tokens`, and `stream: false`. The Gemini API key is loaded through `ISecretStore` and sent as a bearer token. Responses use the chat completion `choices[0].message.content` and optional `usage.prompt_tokens`, `completion_tokens`, and `total_tokens` fields. There is no switch to Google's native `generateContent` protocol.

When the selected model supports it and `SupportsStructuredOutput` is enabled, a supplied schema becomes `response_format: { type: "json_schema", json_schema: { name, schema, strict: true } }`. No tools, thought signatures, image/audio requests, or streaming are implemented in this adapter.

Keep the host's configured model ID; choose or change it in provider settings after checking account access. Missing keys, retired models, unsupported parameters, and quota failures produce safe errors. A connection test is a small inference call and may consume quota. Follow the shared [host setup and limits](README.md).

Protocol verified against Google's official [OpenAI compatibility documentation](https://ai.google.dev/gemini-api/docs/openai) on 2026-09-12.
