# Contributing

Build and test using the commands in README. Tests use synthetic data and mocked HTTP responses; a contributor should not need production credentials.

Open a focused issue describing the business capability or regression, then submit a small pull request with the behavior change and relevant validation. Add tests for calculations, data fences, date boundaries and vendor response parsing where they change. Keep source nullable, async for IO, cancellation-aware and formatted with normal C# conventions.

Never include proprietary host code, real customer records, production SQL dumps, credentials, connection strings or private infrastructure details. Place proprietary adapters in their own repositories. Do not add a general SQL execution tool or an AI-triggered business write path.

When adding a provider, implement `IAIProvider`, keep vendor types outside core, use `ISecretStore`, bound inputs/outputs and sanitize exceptions. When adding a connector, independently verify tenant/branch/user permissions before storage reads and filter before aggregation.

Before proposing a release, run build, tests, dependency audit, the full-history secret scan and an IP/data provenance review. Contributions are provided under the project's Apache-2.0 license. Report vulnerabilities privately according to SECURITY.md.
