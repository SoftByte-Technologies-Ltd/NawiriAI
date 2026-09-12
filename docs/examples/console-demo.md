# Run the synthetic business demo

From the repository root, with the .NET 8 SDK or a later compatible SDK:

```sh
dotnet run --project samples/NawiriAI.SampleBusiness -- --date 2026-09-12
dotnet run --project samples/NawiriAI.SampleBusiness -- --date 2026-09-12 "How much did we sell today?"
dotnet run --project samples/NawiriAI.SampleBusiness -- --date 2026-09-12 "Show top 5 products this month"
dotnet run --project samples/NawiriAI.SampleBusiness -- --date 2026-09-12 "Compare sales this month to last month"
dotnet run --project samples/NawiriAI.SampleBusiness -- --date 2026-09-12 "Show low stock"
```

With no provider configuration, answers use deterministic aggregates and make no network calls. Without `--date`, the demo uses the current UTC calendar date. The connector generates 120 inclusive days ending on that date. A fixed default generator seed of `1729` makes records reproducible for a chosen date. Output generation timestamps and correlation IDs reflect the run.

With `--date 2026-09-12`, main-branch sales today are **KES 3,790** across three transactions. September-to-date sales are **KES 30,560**, compared with **KES 74,870** for the full previous month. The change is **KES -44,310** (approximately -59.18%); the periods contain different numbers of days. The five highest-revenue products are Demo Rice, Demo Coffee, Demo Soap, Demo Bread and Demo Tissue. Machine-readable checkpoints are in `samples/NawiriAI.SampleBusiness/sample-data/expected-2026-09-12.json` and are covered by the synthetic connector tests.

The context is fixed by the sample application to the fictional `demo-company` tenant, `demo-main` branch, and registered `demo-owner` user. The sample is a local console application, not an authentication system. Real hosts must authenticate callers and create their contexts from trusted access grants.

## Optional provider narration

Set environment variables in your own process or development secret tooling:

| Variable | Meaning |
|---|---|
| `NAWIRIAI_PROVIDER` | `none` (default), `gemini`, `openai`, `huggingface`, or `local` |
| `NAWIRIAI_MODEL` | A model ID enabled in your account or local server; required when a provider is selected |
| `NAWIRIAI_API_KEY` | Your provider credential, if required; never put it in source or command arguments |
| `NAWIRIAI_DATA_SHARING_APPROVED` | `true` permits the selected provider to receive sanitized aggregates; otherwise no request is sent |
| `NAWIRIAI_ENDPOINT` | Optional provider endpoint; local uses the adapter's loopback OpenAI-compatible default when absent |

`EnvironmentSecretStore` only resolves the named `demo-provider-key` reference to the explicitly mapped environment variable. It cannot resolve arbitrary environment variable names passed as secret references. The provider adapters enforce transport and endpoint rules. Provider configuration errors or outages do not replace authoritative business totals with guesses.

The sample does not set a default remote model ID because availability changes by provider and account. Read the provider documentation for supported model IDs. No live-provider call is needed to run the sample or its tests.

## Data and calculation limits

Products, categories, customers, two branch ledgers, receipt items, payments, expense allocations, current inventory and opening customer balances are invented in `src/NawiriAI.Connectors/SyntheticDataset.cs`. There are no imported production records or private scoring rules. Customer balances are independent opening balances, not balances reconstructed from the 120-day fully paid sales history. Inventory is a current snapshot, not a reconstructed stock ledger.

Transaction tools require dates within fixture coverage. Inventory, customer-balance and health tools require a period ending on the fixture's current date because historical snapshots are unavailable. Historical daily briefs return recorded sales and expenses with an explicit note that historical inventory and receivables are unavailable. Rows are limited to the requested limit; headline totals still cover all matching data. The standard query maximum is 100 rows and 366 days, while this connector's actual transaction coverage is 120 days.

Top products rank revenue descending. Slow-moving products rank selected-period unit sales ascending. Profit is sales less recorded unit costs; gross profit less recorded expenses is a limited illustrative measure, not statutory net income. Comparisons use full requested totals and can involve unequal period lengths. Percentage change is omitted when the previous total is zero. The fixture currency is KES; tax accounting and exchange conversion are not modeled.

Anomaly flags compare transaction revenue with the prior seven days of transactions in the same authorized branch. At least five preceding observations are required. The threshold is the greater of three scaled median absolute deviations, one quarter of the baseline median's magnitude, or one currency unit. Flags identify statistical departures for review and are not proof of fraud. Stock cover is a simple current-units / selected-period daily-unit-sales estimate, not a forecast.

Business health uses transparent conditions: no sales means insufficient data; expenses above gross profit mean attention; low inventory or outstanding balances mean watch; otherwise stable. There is no numeric health score or credit decision.

Supported tools include sales summaries/trends, products, categories, gross margin, period comparisons, inventory/low-stock/stock-cover, payments, customers, receivables/aging, expenses, transaction anomaly checks, briefs and business health. Stock movement, staff performance, discounts and voids/returns are deliberately unsupported because the fixture does not include those records.
