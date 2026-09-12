# Integrating a business data provider

Implement `IBusinessDataProvider` in the system that owns the schema. Advertise only metrics that can be calculated faithfully. The method signature is:

```csharp
Task<BusinessDataResult> ExecuteAsync(
    BusinessQuery query,
    BusinessSecurityContext security,
    CancellationToken cancellationToken = default);
```

For every call:

1. Validate the query with `QueryPolicy` and authenticate the user against the host's authoritative identity/ACL source. A context DTO alone is not proof of identity.
2. Verify company membership, active branch membership and the required right. Bind storage parameters from the verified scope, never the question.
3. Filter before aggregation. Reuse existing financial definitions, handling refunds, voids, taxes, discounts and currency consistently with the host's reports.
4. Apply query date/row bounds and command timeout. Pass cancellation through IO. Use least-privilege read-only access.
5. Return `BusinessDataResult` using exactly the verified scope, query metric and period. Give the result a source label and timestamp. Do not convert missing data into zero unless the business semantics justify zero.

```csharp
var router = new ProviderRouter(approvedProviderImplementations);
var service = new IntelligenceService(dataAdapter, router,
    audit: metadataAuditSink, timeZone: businessTimeZone);
BusinessAnswer answer = await service.AskAsync(
    question, contextFromAuthenticatedSession, providerConfiguration, cancellationToken);
```

The adapter controls detailed row labels shown to its authorized caller. Core removes those labels before enrichment. Do not hide PII in measure names, units or other metadata; these fields should be fixed schema labels.

The catalog includes future concepts. Unsupported metrics should not be included in `SupportedMetrics`; core then returns a truthful capability error. A production database connector should map its synthetic example schema or host queries behind this interface. MySQL, PostgreSQL, SQLite and REST integrations need no changes to the core.
