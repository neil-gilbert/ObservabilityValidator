# 🧭 Casablanca

**Declarative “observability contracts” for production systems**

Casablanca lets you codify expectations about your system’s telemetry — for example:  
> “Every successful payment emits a span with `payment.status=success` and completes within 3 s.”

It then validates those expectations against your observability backend (Datadog, OpenTelemetry Collector, etc.) automatically.

You can think of it as **tests for your telemetry**: it ensures that the data you rely on for debugging, alerting, and AI-driven analysis is actually being emitted and correct.

---

## 💡 Why

Most teams test their *code*, but not their *observability*.

You might have OpenTelemetry traces, Datadog dashboards, and SLOs — but how do you **know** they’re accurate after a deploy?

Common issues include:

- Missing or renamed spans  
- Broken tags after refactors  
- Latency metrics not recorded correctly  
- Attribute values inconsistent between services  

Casablanca closes that gap by defining **contracts as code** and running them automatically in CI/CD or as a nightly validation job.

---

## ⚙️ How it works

1. You define YAML **contracts** that describe what good telemetry looks like.  
2. You configure **providers** for where to fetch telemetry (Datadog, OTel Collector, etc.).  
3. The CLI tool loads the config and contracts, then queries each provider.  
4. Each contract is validated:
   - Are expected spans present?
   - Do they have required tags?
   - Do tag values match expectations?
   - Are latency or frequency thresholds respected?
5. The CLI prints a pass/fail summary and exits non-zero if anything fails.

---

## 🧩 Example contract

```yaml
version: 1
contracts:
  - name: "Payment Flow Success"
    description: "Ensure successful payment traces include key spans and attributes"
    query: "service:payment-api operation_name:POST /payments"
    window:
      minutes: 15
    expected_spans:
      - name: "POST /payments"
        service: "payment-api"
        min_count: 50
        max_latency_ms: 3000
        tags:
          - key: "payment.status"
            expected: "success"
          - key: "customer.id"
            required: true

      - name: "adyen.charge"
        service: "adyen-client"
        tags:
          - key: "adyen.response_code"
            expected: "200"
          - key: "adyen.currency"
            expected_any_of: ["GBP", "EUR"]
```
```yaml
version: 1
providers:
  - name: datadog-prod
    type: Datadog
    enabled: true
    settings:
      api_url: "https://api.datadoghq.com"
      api_key_env: "DD_API_KEY"
      app_key_env: "DD_APP_KEY"

  - name: otel-local
    type: OtelCollector
    enabled: false
    settings:
      endpoint: "http://localhost:4318"
  
  - name: honeycomb-eu-prod
    type: Honeycomb
    enabled: false
    settings:
      api_url: "https://api.eu1.honeycomb.io"
      api_key_env: "HONEYCOMB_API_KEY"
      dataset: "traces"
```

## Quick start

### Install via NuGet (shift-left)

- Libraries for apps/tests:
  - `dotnet add package Casablanca.Engine`
  - `dotnet add package Casablanca.Abstractions`
  - `dotnet add package Casablanca.Testing`
  - Optional providers (choose as needed):
    - `dotnet add package Casablanca.Providers.Honeycomb`
    - `dotnet add package Casablanca.Providers.Datadog`

- CLI as a dotnet tool:
  - `dotnet tool install -g Casablanca.Cli`
  - Run: `ov --contracts <path> --config <path>`

### 1. Clone & build

```bash
git clone https://github.com/neil-gilbert/Casablanca.git
cd Casablanca
dotnet build Casablanca.sln
```

### 2. Set environment variables

```bash
export DD_API_KEY=your_datadog_api_key
export DD_APP_KEY=your_datadog_app_key

# For Honeycomb (EU)
export HONEYCOMB_API_KEY=your_honeycomb_api_key
```

### 3. Run the validator

```bash
dotnet run --project src/Casablanca.Cli \
  -- --contracts=contracts/observability-contracts.yaml \
     --config=contracts/telemetry-config.yaml
```

### 4. Interpret results

```bash
=== Provider: datadog-prod ===
✅ [Payment Flow Success] All expected spans/tags satisfied.
❌ [Checkout Flow Observability] 2 validation issue(s) found.
   - Span 'POST /checkout' missing required tag 'cart.items.count'.
   - Span 'POST /checkout' exceeded MaxLatencyMs (4300ms > 3000ms)
```

## CI integration

Casablanca exits non-zero if any contract fails — perfect for CI/CD pipelines.

Example GitHub Action:

```yaml
name: Validate Observability

on:
  schedule:
    - cron: "0 3 * * *"
  workflow_dispatch:

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - name: Run validator
        env:
          DD_API_KEY: ${{ secrets.DD_API_KEY }}
          DD_APP_KEY: ${{ secrets.DD_APP_KEY }}
        run: |
          dotnet run --project src/Casablanca.Cli \
            -- --contracts=contracts/observability-contracts.yaml \
               --config=contracts/telemetry-config.yaml
```


---

TODO
- Add a **GitHub Action** wrapper (`action.yml`) so people can use it as `uses: your-org/observability-validator@v1`.
- Sketch how an **AI layer** would sit on top (e.g. CodePunk tool that reads these results and suggests new contracts or fixes).
