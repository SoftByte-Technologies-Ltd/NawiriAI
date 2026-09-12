# Provider data privacy

The host decides which vendor and endpoint may receive business information. No automatic provider switch occurs. A configured but unapproved provider is not called.

Core sends the tool name, calendar periods and bounded aggregate measures. It excludes the original user prompt, tenant/branch/user identifiers, row labels and source metadata. Rows are assigned anonymous ordinals. Sensitive information must not be placed inside fixed measure names or units by an adapter.

Provider keys are resolved through `ISecretStore` immediately before authenticated HTTP requests. Transport returns generic error messages instead of remote error bodies. API logging records only request/tool/provider/model/status/duration/token metadata. Hosts must decide audit retention, access controls and whether additional tracing is appropriate.

OpenAI Responses requests set `store:false`. This flag is not a universal no-retention promise: review the selected provider's current terms and account controls for your deployment. Gemini, Hugging Face and compatible endpoints have their own policies. No live provider request is required for building or testing this project.

Only deterministic figures are authoritative. Numeric model commentary is discarded, while qualitative commentary remains fallible. A provider timeout or outage leaves the structured business facts available.
