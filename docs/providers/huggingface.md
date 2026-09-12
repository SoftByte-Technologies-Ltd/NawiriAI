# Hugging Face

`HuggingFaceProvider` targets the hosted Inference Providers router:

```text
https://router.huggingface.co/v1/chat/completions
```

Requests use the configured model ID, `messages`, `max_tokens`, and `stream: false`. The bearer token comes from `ISecretStore`; it must have permission to call Inference Providers. Model IDs may use the Hugging Face repository ID and supported routing suffix, such as `organization/model:provider`. The adapter preserves the configured value exactly.

The router returns the chat completion text and optional `prompt_tokens`, `completion_tokens`, and `total_tokens`. For deployments with structured output support, set `SupportsStructuredOutput` and provide a JSON schema. It is sent as `response_format` with `type: "json_schema"` and a strict schema envelope. Availability depends on the selected model and backing inference provider; the adapter does not retry with a different provider or drop the format after a rejection.

The adapter does not use the legacy Hugging Face text-generation endpoint or the beta Responses endpoint. A successful connection probe verifies the configured routed model using a small inference call, which may consume credits. Follow the shared [host setup and limits](README.md).

Protocol verified on 2026-09-12 against the official [Chat Completion specification](https://huggingface.co/docs/inference-providers/en/tasks/chat-completion) and [Inference Providers router guide](https://huggingface.co/docs/inference-providers/en/index).
