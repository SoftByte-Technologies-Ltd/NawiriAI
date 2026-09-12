# Security policy

Supported release line: 0.1.x. Do not include customer data, tokens or production logs in public issues. Use GitHub private vulnerability reporting when enabled; otherwise contact the repository maintainer privately before sharing a reproduction containing sensitive information.

## Trust boundaries

The business host owns authentication, tenant membership, branch access and rights. Never construct `BusinessSecurityContext` from client JSON, a prompt or model output. Core enforces query bounds and permissions. Each data adapter must independently verify the asserted identity against its host's trusted ACL and filter records before aggregation. The synthetic adapter is an example of this second check.

There is no SQL tool or business write contract. Unknown structured fields and tools are rejected. Prompt-pattern rejection is defense in depth; authorization does not depend on prompts or model compliance. Metadata audit records omit raw prompts, secrets and business amounts.

Only explicitly approved providers receive data. Inject `ISecretStore`; use the host's encrypted store or vault. Keys must not appear in model messages, exception text, UI configuration responses or Git. Disable HTTP redirects in provider clients. Custom endpoints must be controlled by a trusted administrator and restricted through the host's egress policy.

## Demo boundary

The API is explicitly synthetic. `NAWIRIAI_DEMO_MODE=true` and a randomly generated `NAWIRIAI_DEMO_TOKEN` are required. Its fixed server-side demo identity is not production authentication. A production host must replace `IBusinessSecurityContextAccessor` and the authentication scheme, use TLS, apply its own user/tenant rate limits, configure audit retention and use least-privilege read-only database credentials.

See [fencing](docs/security/data-fencing.md) and [provider privacy](docs/security/provider-data-privacy.md). Run the security tests and secret-history scanner before release.
