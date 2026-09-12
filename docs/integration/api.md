# Synthetic API

Enable the host with `NAWIRIAI_DEMO_MODE=true` and a random `NAWIRIAI_DEMO_TOKEN` of at least 32 characters. Optional `NAWIRIAI_DEMO_DATE=2026-09-12` fixes the business clock/dataset date. `NAWIRIAI_TIMEZONE` defaults to Africa/Nairobi. The demo date is intentionally fixed for the process lifetime; restart to regenerate for a later day.

All business endpoints use `Authorization: Bearer <runtime demo token>`. The token is supplied out of band and never checked into the repository. Company, branch, user and rights come from the server's fixed synthetic identity, not request bodies.

| Endpoint | Body / purpose |
|---|---|
| GET `/api/v1/health` | Public status with no business records |
| GET `/api/v1/providers` | Provider capabilities, configured model and available tools; no credentials |
| POST `/api/v1/chat` | `{"question":"What were my sales this month?"}` |
| POST `/api/v1/query` | Strict query below |
| GET `/api/v1/insights/daily` | Yesterday's deterministic brief |
| GET `/api/v1/business-health` | Transparent synthetic operational indicators |

```json
{
  "metric": "TopProducts",
  "period": { "start": "2026-09-01", "end": "2026-09-12" },
  "limit": 10
}
```

Allowed query fields are `metric`, `period`, `limit` and `comparisonPeriod`. A comparison requires `metric: "PeriodComparison"` and a second inclusive period. Extra fields, duplicate properties, numeric enum values and invalid ranges are rejected.

Answers include `facts`, `displayText`, optional `narrative`, `providerStatus`, `correlationId` and optional token `usage`. Facts include scope, period, measures, rows, source and generation time. Render `displayText` as authoritative, and show `narrative` separately.

Errors use Problem Details and a correlation ID: 400 invalid query, 401 unauthenticated, 403 unauthorized, 422 unsupported capability, 429 rate limited, 503 provider unavailable when applicable. Provider enrichment normally degrades to an answer with `providerStatus: "unavailable"`. No raw exception bodies are exposed.

Business requests are limited to 60 per minute per remote address and Kestrel caps request bodies at 8 KiB. Behind a reverse proxy, configure trusted forwarded headers and production rate policies in a custom host. The included demo authentication is not a replacement for production identity.
