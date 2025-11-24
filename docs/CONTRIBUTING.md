# Contributing to ccxt.collector

Thank you for helping improve ccxt.collector. This guide explains the project scope and the way we work so contributors can be effective and consistent.

## Project scope and goals

- Purpose: Reimplement the exchanges available in ccxt/ccxt (C#) in this repository, aligned to our codebase and unified API.
- Focus on essential features only; avoid scope creep and unnecessary complexity.
- Keep exchanges organized by region under `src/exchanges/<region>/<exchange>/`.

## Architecture overview

This project is a .NET library for real-time cryptocurrency market data collection via WebSocket across many exchanges, providing unified data models and optional technical indicators.

High-level architecture

```
Application → CCXT.Collector Library → Exchange WebSocket APIs
                 ├── Callbacks (OnOrderbook, OnTrade, OnTicker, OnCandle)
                 ├── Indicators (SMA, EMA, RSI, MACD, ...)
                 ├── Data Transformation (Unified Models)
                 └── WebSocket Management (Connection, Subscriptions, Auth)
```

Project structure (key folders)

```
src/
├─ core/        # Abstractions (IWebSocketClient, WebSocketClientBase)
├─ models/      # Market (orderbook, ticker), Trading (account, orders)
├─ indicators/  # Trend, Momentum, Volatility, Volume, MarketStrength
├─ utilities/   # JsonExtension, TimeExtension, Statistics, CLogger
└─ exchanges/   # Exchanges by country (kr/, us/, cn/, ...)
```

Core components

- IWebSocketClient: identity, connect/disconnect, subscribe methods, and event callbacks.
- WebSocketClientBase: reconnection with exponential backoff; dynamic buffer; subscription tracking and restoration; thread-safe operations; optional batch subscriptions.
- Unified models: SOrderBooks, STradeItem, STicker, SCandleItem, etc.

### Non-goals

- Do not add features that are not required by the standardized collector workflows.
- Avoid premature optimization; performance work should follow real measurements.
- Do not introduce breaking API changes without a documented migration.

## Prioritization policy

We implement exchanges in the following order of priority:

1. Included and maintained in ccxt/ccxt C# bindings.
2. High user demand and market share (global liquidity, reliability).
3. WebSocket support available or stable REST for core market data.
4. Lower maintenance risk (clear docs, stable APIs, reasonable rate limits).

Examples that typically score high: Binance, OKX, Bybit, Bitget, Kraken, Coinbase. Regional priorities may elevate local leaders.

## Architecture and API contract

- The core contract is `IExchange` (standardized API v1.1.6+). Implement its methods consistently.
- If an exchange cannot fully implement the contract yet, throw `NotImplementedException` and mark the file header metadata accordingly.
- Use the file header metadata block at the top of exchange implementations:
  ```
  // == CCXT-COLLECTOR-META-BEGIN ==
  // EXCHANGE: <name>
  // IMPLEMENTATION_STATUS: FULL | PARTIAL | SKELETON
  // PROGRESS_STATUS: DONE | WIP | TODO
  // MARKET_SCOPE: spot | futures | margin | ...
  // NOT_IMPLEMENTED_EXCEPTIONS: <count>
  // LAST_REVIEWED: <yyyy-mm-dd>
  // == CCXT-COLLECTOR-META-END ==
  ```

## Implementation methodology

Follow this lightweight, repeatable loop:

1. Select exchange using the prioritization policy above; confirm it exists under ccxt/ccxt C#.
2. Create a minimal skeleton: constructor, identity (name/url), auth placeholders, and stubs with `NotImplementedException` for non-essential parts.
3. Implement essential features only:
  - Public market data: markets/symbols, tickers, trades, order book, candles.
  - Mapping: convert raw API responses to standard ccxt.collector models with strict time/number parsing.
4. Add tests: one happy path + one edge case (nulls/empty, rate limit/backoff path). Mark credentialed tests skippable if secrets are absent.
5. Update documentation: `docs/TASK.md` status by region/exchange; add release note if user-visible.
6. Self-review with quality gates; open PR.

## Developer quick start

Set up the repository and run local builds/tests.

```bash
git clone https://github.com/YOUR_USERNAME/ccxt.collector.git
cd ccxt.collector
git remote add upstream https://github.com/odinsoft-lab/ccxt.collector.git
dotnet restore
dotnet build
dotnet test
```

Minimal usage example (for ad-hoc local testing):

```csharp
using CCXT.Collector.Exchanges;

var client = new BinanceWebSocketClient();
client.OnOrderbookReceived += (symbol, ob) => Console.WriteLine($"{symbol} bid: {ob.Bids[0]?.Price}");
await client.ConnectAsync();
await client.SubscribeOrderbookAsync("BTC/USDT");
```

## Coding standards

- Language policy: All Markdown must be English; every function/method must include an English comment (XML doc recommended) describing purpose, inputs, outputs, and error modes.
- XML documentation: All public members require XML docs. Prefer `<inheritdoc/>` where the interface already documents the member; write explicit summaries for constructors, helpers, and any non-interface members.
- Style: Follow existing code style; do not reformat unrelated files. Keep changes minimal and focused.
- Naming: Use `X<Exchange>` for exchange classes, e.g., `XKraken`, and set `ExchangeName` to the lowercase slug used in HTTP clients.

### Recommended C# doc comment template

```csharp
/// <summary>
/// Short description of what the method does.
/// </summary>
/// <param name="paramName">Meaning and units if applicable.</param>
/// <returns>What is returned, including nullability.</returns>
/// <exception cref="ExceptionType">When and why it is thrown.</exception>
```

## Adding a new exchange

1. Create `src/exchanges/<region>/<exchange>/X<Exchange>.cs` using an existing skeleton as a template.
2. Fill in metadata header, `ExchangeName`, `ExchangeUrl`, and authentication boilerplate (`Encryptor`, signature helpers).
3. Implement the standardized methods of `IExchange` (market data, account, trading, funding). For incomplete parts, throw `NotImplementedException`.
4. Add English XML docs or `<inheritdoc/>` for all public members.
5. Update documentation:
   - `docs/EXCHANGES.md` and `docs/TASK.md` to reflect implemented/unimplemented status by region.
   - Add or update release notes in `docs/releases/` if the change is user-visible.
6. Add or update unit tests under `tests/` for the new exchange (happy path + 1–2 edge cases).

## Security for contributors

Security is a first-class concern. Do not introduce insecure patterns.

- Credentials: Never hardcode or commit API keys. Prefer environment variables, local user-secrets, or a vault (Azure Key Vault, AWS Secrets Manager).
- Input validation: Validate symbols and external inputs; reject malformed payloads and unexpected types.
- Rate limiting/throttling: Respect exchange limits; add simple local throttles as needed.
- Logging: Do not log secrets; redact tokens/keys; lower verbosity for sensitive paths.
- Reviews: Call out security-impacting changes in PR descriptions.

Checklist

- [ ] Secure credential storage (env/vault/user-secrets)
- [ ] Validation for external data (symbols, message shapes)
- [ ] Throttling where applicable
- [ ] Sensitive logging redaction
- [ ] Regular dependency/runtime updates

## TASK.md maintenance

- File: `docs/TASK.md` tracks implementation status by region and exchange.
- Recommended format: a table with columns `Region | Exchange | Status(FULL/PARTIAL/TODO) | Notes`.
- Update the row whenever status changes; keep notes brief and actionable (e.g., "no WS for candles; REST only").

## Build, test, and quality

- Build with `dotnet build`; run tests with `dotnet test`.
- Do not introduce new build warnings. In particular, address CS1591 by adding XML docs or `<inheritdoc/>`.
- Keep public behavior backward compatible unless a release note calls out a change.

### Quality gates (must pass before merge)

1. Build: succeeds on all target frameworks with zero warnings.
2. Tests: new/updated tests pass locally; avoid flakiness (no real network in unit tests).
3. Docs: English-only; updated `docs/TASK.md` and release notes if user-visible.
4. Lint/Analyzers: documentation warnings (e.g., CS1591) resolved or intentionally suppressed with justification.

## Commit and release workflow

- You run git add/commit manually; do not automate commits or pushes.
- Write commit messages in English, in imperative mood (e.g., "Add Upbit order book snapshot mapper").
- Update `docs/TASK.md` and `docs/releases/x.y.z.md` with any user-visible changes.

### Release notes rules

- One file per version: `docs/releases/x.y.z.md`.
- Use sections: Added, Changed, Fixed, Removed, Performance, Security, Migration.
- Keep entries concise and reproducible; add issue/PR numbers in parentheses when relevant.

## Repository layout

- Standard models live in `src/models`.
- Exchange-specific or non-standard code lives in `src/exchanges/<region>/<exchange>`.
- Shared utilities in `src/utilities`.
- Tests in `tests` mirror the `src` tree.

## PR checklist

- [ ] Code builds without warnings on all target frameworks.
- [ ] English XML comments for all new or changed functions.
- [ ] Tests added or updated; all tests pass locally.
- [ ] Docs updated: `docs/TASK.md` and release notes if applicable.
- [ ] Exchange mapping updated if adding a new exchange.

## Communication

- Use GitHub issues and PRs. Keep discussion in English for consistency.

---

## Release & deployment guide

This section consolidates publishing and deployment guidance. Prefer the repository scripts for common tasks.

### NuGet publishing

Prerequisites

1. .NET SDK installed
2. NuGet API key available (environment variable or secure script)
3. PowerShell available on Windows

Pre-upload checklist (generic)

- [ ] Bump version in `src/ccxt.collector.csproj` (Version/AssemblyVersion/FileVersion)
- [ ] Update PackageReleaseNotes with a concise summary
- [ ] Update docs: README (install line), TASK.md (if user-visible), release notes file
- [ ] Build Release and run tests locally
- [ ] Address warnings; documentation (CS1591) resolved
- [ ] Security checklist reviewed (no secrets, vaults configured for CI)

Scripts (preferred)

- `scripts/publish-nuget.ps1` and `.bat`: build, test, pack, and publish
- `scripts/unlist-nuget.ps1` and `.bat`: unlist a version
- `scripts/nuget-config.ps1.example`: template to manage the API key

Manual commands (alternative)

```bash
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test tests/ccxt.tests.csproj -c Release
dotnet pack src/ccxt.collector.csproj -c Release -o ./nupkg

# Upload (skip-duplicate recommended for CI)
dotnet nuget push ./nupkg/CCXT.Collector.{VERSION}.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY \
  --skip-duplicate
```

Post-upload tasks

- [ ] Verify package on NuGet.org
- [ ] Test installation in a clean sample project
- [ ] Create GitHub release tag and attach release notes
- [ ] Announce in appropriate channels

Package contents (verify)

- Sources from `src/`
- README.md, LICENSE.md, package icon
- Target frameworks (e.g., net8.0, net9.0, net10.0 as configured)
- Correct dependencies

Security notes for releases

- Never commit API keys; keep `scripts/nuget-config.ps1` untracked (use the example)
- Prefer GitHub Secrets or a vault for CI/CD
- Rotate API keys periodically

### Appendix A — Deployment recipes (optional)

These are operational examples for running the collector as an app/service. Adapt as needed; they are not required for library usage.

- Linux publish (dotnet publish), Supervisor unit example, and logrotate snippet
- Dockerfile and docker-compose usage
- Windows Service basics with `sc.exe`
- Cloud: Azure App Service, AWS Elastic Beanstalk, and a basic Kubernetes Deployment/Service manifest

### Appendix B — Configuration, monitoring, troubleshooting

Environment configuration example (`appsettings.Production.json`)

- Common keys: websocket retry/backoff, auto-start options, logging levels

Monitoring

- Health checks endpoint
- Optional integrations: Application Insights, Prometheus metrics

Troubleshooting quick tips

- Port in use: identify with netstat; stop conflicting process
- Linux permissions: executable bits and ownership
- Service won’t start: inspect logs, config, dependencies, firewall
