# Architecture

NawiriAI is a modular .NET 8 library with an optional ASP.NET Core demo host. A POS transaction never depends on an AI request.

| Module | Responsibility |
|---|---|
| Abstractions | Immutable security scope, business query/result DTOs, provider/secret/audit interfaces |
| Core | Bounded intent planning, strict query validation, capability registry, authorization, orchestration and fact rendering |
| Analytics | Transparent deterministic comparisons, anomaly signals and operational health indicators |
| Connectors | Synthetic data adapter and explicitly mapped environment secret store |
| Providers.Common | Bounded HTTP transport, validation, timeout and safe errors |
| Providers.Gemini / OpenAI / HuggingFace | Vendor wire formats; compatible local endpoint lives in OpenAI project |
| Api | Explicit synthetic host, bearer authentication, server-created identity, rate limits and metadata logging |
| SampleBusiness | Credential-free console demo and optional provider configuration |

One interaction executes one approved business query. The query cannot carry tenant, branch, user, permission, endpoint, SQL or mutation fields. The authenticated host supplies scope separately. A production adapter must check that scope against its authoritative identity and storage system before selecting records.

Core validates tool permissions, inclusive calendar ranges, comparison fields and result limits. Defaults are 100 rows, 366 days, a 15-second data timeout and 16,000 characters of provider input. Result identity, tool, period and row bounds are verified before provider enrichment. Read-only collections snapshot provider results.

Facts and interpretation have separate fields. Provider input contains aggregate measures, date ranges and anonymous row ordinals. It excludes the original question, company/branch/user identifiers, source metadata and row labels. Numeric model commentary is discarded. This is not a guarantee that qualitative language is correct; the deterministic result remains authoritative.

The host must propagate cancellation into real database commands. Core stops awaiting after its deadline, but cannot forcibly terminate an adapter that ignores cancellation. No automatic retries or cross-provider fallback are performed. Hosts can add concurrency limits or circuit breakers to their own infrastructure without changing business logic.

The optional audit sink has a two-second delivery deadline and is best effort. Sink failures produce a generic diagnostic and never replace the original answer, authorization denial or cancellation. Hosts requiring durable audit admission must enforce that requirement before invoking the service.

Provider endpoints and secret references are configuration owned by administrators. HTTP redirects must be disabled on injected clients. Public code has no knowledge of proprietary table names, licensing, financial scoring or payment workflows.

See [architecture decisions](docs/architecture/decisions.md) and [adapter integration](docs/integration/business-data-provider.md).
