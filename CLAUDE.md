# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Important**: 
1. Always read and understand the documentation files in the `docs/` folder to maintain comprehensive knowledge of the project. These documents contain critical information about the project's architecture, implementation details, and version history.
2. After completing each task or implementation, immediately update the relevant documentation to reflect the changes. Keep all documentation synchronized with the current state of the codebase.

## Project Overview

CCXT.Collector is a .NET library that connects to cryptocurrency exchanges worldwide via WebSocket to receive real-time market data (orderbook, trades, ticker, etc.) and delivers it to callback functions using unified data classes. Additionally, it analyzes the data per exchange and market to calculate technical indicators in real-time, providing these indicator values through callbacks. This allows developers to handle both raw market data and technical analysis from different exchanges with a consistent interface.

### Recent Major Updates (2025-08-14)
- **Documentation Consolidation**: Streamlined documentation structure
  - Consolidated 13 documents into 4 comprehensive guides
  - Created unified GUIDE.md with architecture, security, API reference, and contributing sections
  - Integrated security best practices and critical vulnerability documentation
  - Final structure: releases\README.md, DEPLOYMENT.md, GUIDE.md, ROADMAP.md
- **Bitget WebSocket Fixes**: Resolved implementation issues
  - Fixed instType to use "sp" instead of "SPOT"
  - Fixed trade data processing: now correctly handles array of objects (not array of arrays)
  - Symbol format confirmed: BTCUSDT (no slash, no suffix like _SPBL)
  - Test results: 4/5 tests passing (ticker, orderbook, trades, connection work)

### Previous Updates (2025-08-13)
- **v2.1.7 Release**: Fixed WebSocket implementations for multiple exchanges
  - Fixed Crypto.com, Coinone, Coinbase WebSocket protocols
  - Coinbase: Changed from `level2` to `level2_batch` for public access
  - Coinone: Fixed v2 API compatibility with proper response_type handling

### Previous Updates (2025-08-12)
- **v2.1.5 Release**: Complete migration from Newtonsoft.Json to System.Text.Json
  - 20-30% faster JSON parsing, 15-25% less memory usage
  - JsonExtensions utility class with safe property access methods
  - All 15 exchanges tested and working with new implementation
  - Unified subscription handling with `MarkSubscriptionActive` method
  - Batch subscription system implemented for 11 exchanges
  - Automatic resubscription on reconnection via `RestoreActiveSubscriptionsAsync`
- **v2.1.3-2.1.4 Updates**: Complete WebSocket implementation for 15 major exchanges
  - Full implementations: Gate.io (JSON protocol), Bittrex (SignalR protocol)
  - Standardized data models: STrade, STradeItem, SCandle, SCandleItem
  - Fixed all build errors with proper model and method usage
- **Security Analysis Conducted**: Identified critical security issues
  - Plain text API key storage needs urgent attention
  - Missing secure credential management system
  - Input validation and sanitization required
- **Test Coverage Status**: 100% coverage for major exchanges
  - All 15 major exchanges have unified WebSocket test suite
  - Test base framework (`WebSocketTestBase`) provides consistent testing
  - Integration tests validate connection, subscription, and data reception
- **Code Reorganization**: Complete restructuring of source code:
  - Core abstractions and infrastructure in `Core/` folder
  - Data models in `Models/Market/`, `Models/Trading/`, `Models/WebSocket/`
  - Technical indicators in `Indicators/` with subcategories
  - Utility classes in `Utilities/` folder
  - Channel management system with `ChannelManager` and `SubscriptionInfo`
- **Exchange Implementations (15 Total)**:
  - ✅ Complete: Binance, Bitget, Bithumb, Bittrex, Bybit, Coinbase, Coinone, Crypto.com, Gate.io, Huobi, Korbit, Kucoin, OKX, Upbit
  - OkEX merged with OKX (rebranded)

## Build and Development Commands

### Building the Project
```bash
# Build the solution
dotnet build

# Build specific target framework
dotnet build -f net10.0

# Build in Release mode
dotnet build -c Release

# Restore packages
dotnet restore
```

### Running Tests
```bash
# Run tests
dotnet run --project tests/ccxt.tests.csproj

# Run samples
dotnet run --project samples/ccxt.samples.csproj
```

### Publishing
```bash
# Create NuGet package
dotnet pack src/ccxt.collector.csproj -c Release

# Publish for specific runtime (example for Ubuntu)
dotnet publish -c Release -r ubuntu.18.04-x64 -f net8.0
```

## Architecture Overview

### Core Components

1. **Core Framework** (`src/Core/`):
   - **Abstractions** (`Core/Abstractions/`):
     - `IWebSocketClient.cs` - WebSocket client interface defining callback events
     - `IChannelManager.cs` - Channel management interface
     - `WebSocketClientBase.cs` - Enhanced base implementation with:
       - Dynamic buffer resizing for large messages
       - Exponential backoff reconnection (max 60s)
       - Exchange rate support for multi-currency
       - Automatic subscription restoration on reconnect
       - Subscription tracking with `SubscriptionInfo`
   - **Configuration** (`Core/Configuration/`):
     - `config.cs` - Configuration classes
     - `settings.cs` - Application settings
   - **Infrastructure** (`Core/Infrastructure/`):
     - `ChannelManager.cs` - Advanced subscription management with:
       - Batch subscription mode for efficient connection
       - Channel statistics and monitoring
       - Automatic idle exchange disconnection
       - Pending subscription queue management
     - `factory.cs` - Factory pattern implementations
     - `logger.cs` - Logging infrastructure
     - `selector.cs` - Selector utilities

2. **Data Models** (`src/Models/`):
   - **Market Models** (`Models/Market/`):
     - `orderbook.cs` - Order book data structures
     - `ticker.cs` - Ticker data structures
     - `ohlcv.cs` - OHLCV candle data
     - `candle.cs` - Candlestick data
   - **Trading Models** (`Models/Trading/`):
     - `account.cs` - Account/balance structures
     - `trading.cs` - Trading data structures
     - `complete.cs` - Complete order structures
   - **WebSocket Models** (`Models/WebSocket/`):
     - `apiResult.cs` - API result models
     - `wsResult.cs` - WebSocket result models
     - `message.cs` - Message structures

3. **Technical Indicators** (`src/Indicators/`): Organized by category:
   - **Base** (`Indicators/Base/`): `IndicatorCalculatorBase.cs`
   - **Trend** (`Indicators/Trend/`): SMA, EMA, WMA, DEMA, ZLEMA, MACD, SAR
   - **Momentum** (`Indicators/Momentum/`): RSI, CMO, Momentum, ROC, TRIX
   - **Volatility** (`Indicators/Volatility/`): BollingerBand, ATR, Envelope, DPO
   - **Volume** (`Indicators/Volume/`): OBV, ADL, CMF, PVT, VROC, Volume
   - **MarketStrength** (`Indicators/MarketStrength/`): ADX, Aroon, CCI, WPR
   - **Advanced** (`Indicators/Advanced/`): Ichimoku
   - **Series** (`Indicators/Series/`): Various serie classes for indicator data

4. **Utilities** (`src/Utilities/`):
   - `JsonExtension.cs` - Safe JSON property access with:
     - Type-flexible parsing (string/number handling)
     - Unix timestamp normalization (seconds/milliseconds)
     - Array handling with empty/non-empty distinction
     - Diagnostic hooks for debugging
   - `TimeExtension.cs` - Time-related extension methods
   - `LinqExtension.cs` - LINQ extensions
   - `Statistics.cs` - Statistical calculations
   - `logger.cs` - Logging utilities

5. **Exchange Implementations** (`src/exchanges/`): Each exchange has:
   - **WebSocket Client** (e.g., `BinanceWebSocketClient.cs`) - Real-time data streaming
   - **Callback Events**: OnOrderbookReceived, OnTradeReceived, OnTickerReceived, etc.


### WebSocket-based Data Flow Architecture

1. **WebSocket Connection**: 
   - Each exchange has dedicated WebSocket client (e.g., `BinanceWebSocketClient`)
   - Automatic connection management with reconnection support
   - Exchange-specific WebSocket URL and protocol handling

2. **Subscription Management**:
   - Subscribe to specific channels: `SubscribeOrderbookAsync()`, `SubscribeTradesAsync()`, `SubscribeTickerAsync()`
   - Per-symbol subscription with unified interface across exchanges
   - Automatic resubscription on reconnection

3. **Data Reception & Processing**:
   - Raw WebSocket messages processed by exchange-specific handlers
   - Exchange format converted to unified data classes (`SOrderBooks`, `SCompleteOrders`, `STicker`)
   - Incremental updates handled appropriately (orderbook deltas, trade streams)

4. **Callback Delivery**:
   - Direct callback invocation for real-time data delivery
   - Event-driven architecture with typed callbacks for each data type
   - No intermediate queuing - direct delivery to registered handlers

5. **Technical Analysis** (Optional):
   - Calculates technical indicators using received market data
   - Per-exchange and per-market indicator calculation
   - Delivered through same callback mechanism

6. **Error Handling & Recovery**:
   - Automatic reconnection with exponential backoff
   - Error callbacks for monitoring connection health
   - Subscription state management across reconnections

### Real-time Technical Analysis Flow

1. **Data Aggregation**: Collects OHLCV data per exchange and market symbol
2. **Indicator Calculation**: Updates indicator values with each new data point
3. **Series Management**: Maintains historical series data required for indicator calculations
4. **Callback Triggers**: Sends calculated indicator values to registered callbacks
5. **Performance Optimization**: Uses efficient algorithms to minimize calculation overhead

### Key Dependencies

- **Microsoft.Extensions.Configuration**: Configuration management
- **System.Text.Json**: JSON support

### Configuration

The application uses `appsettings.json` for configuration:
- Exchange-specific settings (polling intervals, snapshot counters)
- Auto-start configuration for exchanges and symbols
- WebSocket retry settings and reconnection parameters

### Key Features

- **Real-time WebSocket Streaming**: Primary method for receiving market data with minimal latency
- **Unified Data Classes**: All exchanges return data in the same format, simplifying multi-exchange integration
- **Callback-based Architecture**: Register callbacks to handle incoming data asynchronously
- **Real-time Technical Indicators**: Calculates and delivers technical indicator values per exchange/market
- **Multi-indicator Support**: Over 25 technical indicators available for real-time analysis
- **Automatic Reconnection**: WebSocket connections automatically reconnect on disconnection (10 retry attempts, exponential backoff up to 60s)
- **Incremental Orderbook Updates**: Efficiently processes orderbook deltas to maintain current state
- **Per-market Analysis**: Independent indicator calculation for each exchange and trading pair
- **High Performance**: System.Text.Json for 20-30% faster parsing, 15-25% less memory usage

### Critical Issues (As of 2025-08-12 Analysis)

- **🔴 Security**: Plain text API key storage - needs secure credential management implementation
- **🟢 Testing**: 100% test coverage for major exchanges with unified test suite
- **🟠 Error Handling**: Single parse failure triggers full reconnection - needs threshold
- **🟠 Performance**: Buffer management can be optimized with ArrayPool<byte>
- **🟠 Observability**: Limited metrics injection for channel statistics

### Exchange-Specific Notes

- **Binance**: Full WebSocket support for all market data streams
- **Upbit**: WebSocket for real-time data, REST API fallback available
- **Bitmex**: WebSocket with position and margin trading data streams
- **Deribit**: WebSocket using JSON-RPC protocol for derivatives
- **Bithumb, Gemini, ItBit**: WebSocket implementations for spot trading

### Target Frameworks

The project targets both .NET 8.0 and .NET 9.0, allowing compatibility with different runtime environments.