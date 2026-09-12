# OpenAI

`OpenAIProvider` uses the Responses API:

```text
https://api.openai.com/v1/responses
```

It sends the configured `model`, text messages in `input`, `max_output_tokens`, `stream: false`, and `store: false`. Authentication uses the host's secret store and a per-request bearer token. Provider retention and account policies still apply.

Text is assembled from every `output` item with `type: "message"` and every content part with `type: "output_text"`; reasoning entries are skipped. This avoids assuming that the first output item is assistant text. Usage fields map from `input_tokens`, `output_tokens`, and `total_tokens` when available.

For models enabled for structured output, the request sends `text.format: { type: "json_schema", name, schema, strict: true }`. A refused, incomplete, failed, or empty generation is not returned as a successful business response. There is no Chat Completions fallback, SDK dependency, implicit retry, conversation storage, or `previous_response_id` state.

Select a model that supports Responses and the requested output format. Model IDs are supplied by the host and never hardcoded by this adapter. The connection probe caps output at 64 tokens; a recognized `max_output_tokens` incomplete result confirms connectivity without accepting partial business output. Follow the shared [host setup and limits](README.md).

Protocol verified on 2026-09-12 against official OpenAI [text generation documentation](https://developers.openai.com/api/docs/guides/text) and [Structured Outputs documentation](https://developers.openai.com/api/docs/guides/structured-outputs).
