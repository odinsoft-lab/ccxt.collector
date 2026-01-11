# Releases

This folder stores release notes of CCXT.Collector by version.

## Index

- [v2.x Release Notes](./v2.md) - All v2.x releases (2.0.0 ~ 2.1.12)

## Latest Release: v2.1.12 (2026-01-12)

### Highlights
- **New Exchanges**: Kraken, Bitfinex, Bitstamp fully implemented
- **Observability**: IChannelObserver pattern for metrics and health monitoring
- **DI Support**: Microsoft.Extensions.DependencyInjection integration
- **Test Coverage**: 439 tests (285% increase from v2.1.9)

## Authoring guidelines

- Filename: `v{major}.md` (e.g., `v2.md`, `v3.md`)
- Header: `## x.y.z - YYYY-MM-DD`
- Order: Newest releases at the top
- Sections (use only what you need):
  - Added
  - Changed
  - Fixed
  - Removed
  - Performance
  - Security
  - Migration

## Tips

- Record changes concisely but with enough detail to reproduce.
- If relevant, append major issue or PR numbers at the end of the item in parentheses.
- If there is an impact on backward compatibility, explicitly mark it under "Migration" or "Breaking".
