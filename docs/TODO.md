# CCXT.Collector TODO List

This document tracks remaining tasks in order of priority for future development.

*Last Updated: 2026-01-12*

---

## Current Status Summary

| Metric | Value |
|--------|-------|
| NuGet Version | 2.1.12 |
| Active Exchanges | 16 |
| Total Tests | 439 |
| Build Status | 0 warnings, 0 errors |

---

## 🔴 Critical Priority (Immediate Action Required)

### 1. Security Implementation
**Status**: Not Started
**Impact**: High - Production security vulnerability
**Estimated Effort**: 3-5 days

The current implementation stores API keys in plain text configuration files, which is a critical security vulnerability.

**Required Actions**:
- [ ] Integrate secure credential storage (Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault)
- [ ] Remove plain text API key storage from appsettings.json
- [ ] Implement input validation and sanitization for all user inputs
- [ ] Add authentication token refresh mechanism
- [ ] Document secure credential management best practices

**Files Affected**:
- `src/Core/Configuration/config.cs`
- `src/Core/Configuration/settings.cs`
- `appsettings.json`

---

## 🟠 High Priority (Next Sprint)

### 2. MEXC Full Implementation
**Status**: Partial (connection works)
**Impact**: High - Exchange coverage
**Estimated Effort**: 2-3 days

MEXC WebSocket uses Protocol Buffers (protobuf) format for market data streams.

**Required Actions**:
- [ ] Add `Google.Protobuf` NuGet package
- [ ] Obtain/create MEXC proto definition files
- [ ] Implement protobuf deserialization for market data
- [ ] Update `ProcessMessageAsync` to handle binary protobuf messages
- [ ] Run all 5 MEXC tests and verify passing

**Files Affected**:
- `src/exchanges/cn/mexc/MexcWebSocketClient.cs`
- `src/ccxt.collector.csproj` (add package reference)

**Reference**:
- MEXC API Docs: https://mexcdevelop.github.io/apidocs/spot_v3_en/

### 3. Probit Exchange Implementation
**Status**: Not Started
**Impact**: Medium - Korean market coverage
**Estimated Effort**: 2-3 days

**Required Actions**:
- [ ] Research Probit WebSocket API documentation
- [ ] Create `ProbitWebSocketClient.cs` in `src/exchanges/kr/probit/`
- [ ] Implement subscription methods (ticker, orderbook, trades)
- [ ] Create `ProbitTests.cs` test file
- [ ] Update `docs/TASK.md` status

### 4. Authentication/Private Channels
**Status**: Not Started
**Impact**: High - Feature expansion
**Estimated Effort**: 5-7 days

Currently all 16 exchanges support public channels only. Private channels require authentication.

**Required Actions**:
- [ ] Design authentication interface (API key, secret, passphrase)
- [ ] Implement signature generation per exchange
- [ ] Add private channel subscriptions (orders, positions, balances)
- [ ] Create secure credential storage integration
- [ ] Add authentication tests (with mocked credentials)

---

## 🟡 Medium Priority (This Quarter)

### 5. Code Quality Improvements
**Status**: Ongoing
**Impact**: Medium - Maintainability
**Estimated Effort**: 3-5 days

**Required Actions**:
- [ ] Extract common WebSocket patterns to shared base classes
- [ ] Reduce code duplication across exchange implementations
- [ ] Add missing interface definitions for concrete implementations
- [ ] Apply consistent coding standards across all files
- [ ] Add XML documentation to public APIs

### 6. Comprehensive Logging
**Status**: Not Started
**Impact**: Medium - Debugging/Operations
**Estimated Effort**: 2-3 days

**Required Actions**:
- [ ] Integrate Serilog with structured logging
- [ ] Add log levels configuration
- [ ] Implement sensitive data redaction
- [ ] Add correlation IDs for request tracking
- [ ] Create logging best practices documentation

### 7. Additional Exchange Implementations
**Status**: Ongoing
**Impact**: Medium - Feature expansion
**Estimated Effort**: 2-3 days per exchange

**Priority List** (by trading volume/demand):
- [ ] OKCoinKR (KR) - Korean market
- [ ] BitMEX (SG) - Derivatives
- [ ] Deribit (AE) - Options/Futures
- [ ] Gemini (US) - US market
- [ ] Bitflyer (JP) - Japanese market

---

## 🟢 Low Priority (Future Sprints)

### 8. Performance Benchmark Suite
**Status**: Not Started
**Impact**: Low - Development tooling
**Estimated Effort**: 2-3 days

**Required Actions**:
- [ ] Create BenchmarkDotNet project
- [ ] Add benchmarks for JSON parsing performance
- [ ] Add benchmarks for indicator calculations
- [ ] Add benchmarks for WebSocket message throughput
- [ ] Document baseline performance metrics

### 9. Integration Tests with Test Fixtures
**Status**: Not Started
**Impact**: Low - Test coverage
**Estimated Effort**: 3-5 days

**Required Actions**:
- [ ] Create mock WebSocket server for integration testing
- [ ] Add fixture-based tests for each exchange
- [ ] Implement test data generators
- [ ] Add CI/CD pipeline integration

### 10. DEX Support
**Status**: Not Started
**Impact**: Low - Feature expansion
**Estimated Effort**: 5-10 days per DEX

**Target DEXs**:
- [ ] Uniswap V3 (Ethereum)
- [ ] PancakeSwap (BSC)
- [ ] SushiSwap (Multi-chain)

---

## Quick Start Guide for Next Session

### Recommended Next Tasks (in order):

1. **MEXC Protobuf Support** - Completes partial implementation
   ```bash
   dotnet add src/ccxt.collector.csproj package Google.Protobuf
   ```

2. **Probit Exchange** - Expands Korean market coverage
   - Copy `BitstampWebSocketClient.cs` as template
   - Research API at https://docs.probit.com/

3. **Security Implementation** - Critical for production use
   - Start with Azure Key Vault integration
   - Use `Microsoft.Extensions.Configuration.AzureKeyVault`

---

## Completed Tasks Archive

### v2.1.12 (2026-01-12)
- [x] Implemented Kraken exchange (WebSocket API v2, 5 tests)
- [x] Implemented Bitfinex exchange (WebSocket API v2, 5 tests)
- [x] Implemented Bitstamp exchange (WebSocket API v2, 5 tests)
- [x] MEXC exchange partial (connection works)
- [x] Implemented IChannelObserver for observability (18 tests)
- [x] Implemented Dependency Injection support
- [x] Bitget V2 API migration completed
- [x] Increased test coverage from 154 to 439 tests (285%)
- [x] Fixed all build warnings (0 warnings, 0 errors)
- [x] Verified all 16 active exchanges
- [x] Published NuGet package v2.1.12
- [x] Updated all documentation

### v2.1.10 (2025-12-15)
- [x] Added OnInfo event for informational logging
- [x] Fixed Binance WebSocket disconnection issue

### Previously Completed
- [x] Error resilience (parse failure threshold)
- [x] Memory optimization (ArrayPool<byte>)
- [x] System.Text.Json migration
- [x] Batch subscription support (11 exchanges)
- [x] Auto-resubscription on reconnect

---

## Notes

- Priority levels: 🔴 Critical > 🟠 High > 🟡 Medium > 🟢 Low
- "Estimated Effort" is development time only, excluding testing and review
- Update this document as priorities change
- Move completed tasks to archive section

---

*This is a living document. Update regularly to reflect current project state.*
