# Local OpenAI-compatible servers

`OpenAICompatibleProvider` is in `NawiriAI.Providers.OpenAI` and has provider ID `local`. Its default complete endpoint is:

```text
http://localhost:11434/v1/chat/completions
```

This matches Ollama's OpenAI-compatible chat interface. The model must already be installed and served by the local runtime; this library does not download models or start a server. Set the model ID to the runtime's exact configured name. Other compatible servers can be used by replacing `Endpoint` with their complete chat completions URL, for example `http://127.0.0.1:1234/v1/chat/completions`.

Loopback HTTP or HTTPS endpoints work without a secret reference. If a reference is supplied, the adapter retrieves and sends that bearer token, failing when the referenced secret is unavailable. Non-loopback endpoints require HTTPS, a secret reference, and data-sharing approval. A server reached through a private LAN address is non-loopback for this check.

The request uses `model`, `messages`, `max_tokens`, and `stream: false`; responses must follow the compatible `choices` structure. JSON schema requests are sent only when the host enables `SupportsStructuredOutput` for the selected model/server. A server advertising JSON mode may not support strict JSON schemas; leave this setting false until the actual deployment supports it.

The host must disable redirects on its injected HTTP handler. The library never routes failed local requests to a cloud provider. Follow the shared [host setup and limits](README.md).

The default protocol was verified against the official [Ollama OpenAI compatibility documentation](https://docs.ollama.com/api/openai-compatibility) on 2026-09-12. Other compatible servers require their own deployment verification.
