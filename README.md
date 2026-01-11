# CCXT.Collector

[![NuGet](https://img.shields.io/nuget/v/CCXT.Collector.svg)](https://www.nuget.org/packages/CCXT.Collector/)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)
[![Downloads](https://img.shields.io/nuget/dt/CCXT.Collector.svg)](https://www.nuget.org/packages/CCXT.Collector/)

A powerful .NET library for real-time cryptocurrency exchange data collection with unified WebSocket streaming and technical indicator analysis.

---

## 📊 Overview

CCXT.Collector is a comprehensive library that connects to cryptocurrency exchanges worldwide via WebSocket to receive real-time market data and calculate technical indicators. It provides a unified interface for handling data from multiple exchanges, making it easy to build trading bots, market analysis tools, and data collection systems.

### ✨ Key Features

- 🚀 **Real-time WebSocket Streaming** - Low-latency market data streaming with exponential backoff reconnection
- 🔄 **Unified Data Classes** - Consistent data format across all exchanges (STicker, SOrderBook, STrade, SCandle)
- 📈 **25+ Technical Indicators** - Real-time calculation per exchange/market with optimized algorithms
- 🔌 **Callback Architecture** - Asynchronous event-driven data handling with typed callbacks
- 🔐 **Automatic Reconnection** - Resilient WebSocket connection management with subscription restoration
- ⚡ **High Performance** - System.Text.Json for 20-30% faster parsing, 15-25% less memory usage
- 📊 **Channel Management** - Advanced subscription tracking with batch mode and statistics
- 🛡️ **Security Ready** - Authentication framework for private channels (implementation in progress)

### 🏢 Supported Exchanges (130 Active)

#### Exchange Status Management
The library now includes automatic exchange status tracking to prevent connections to closed or unavailable exchanges. When attempting to connect to a closed exchange, you'll receive a clear error message with suggested alternatives.

#### Major Exchanges by Region

| Region | Exchanges | Count |
|--------|-----------|-------|
| 🇺🇸 United States | Coinbase, Kraken, Gemini, ~~Bittrex~~*, Poloniex, Phemex, Crypto.com, and 18 more | 25 |
| 🇨🇳 China | Binance*, OKX, Huobi, Bybit, KuCoin, Gate.io, MEXC, Bitget, and 16 more | 24 |
| 🇰🇷 South Korea | Upbit, Bithumb, Coinone, Korbit, Gopax, Probit, OKCoinKR | 7 |
| 🇯🇵 Japan | bitFlyer, Coincheck, Bitbank, Zaif, and 4 more | 8 |
| 🇪🇺 Europe | Bitstamp, Bitfinex, Bitvavo, EXMO, WhiteBIT, and 8 more | 13 |
| 🇬🇧 United Kingdom | Bitfinex, Bitstamp, CEX.IO, Luno, and 3 more | 7 |
| 🇸🇬 Singapore | BitMEX*, Bitrue, Coins.ph, and 5 more | 8 |
| 🌍 Other Regions | Deribit (UAE), BTC Markets (AU), Bitso (MX), NDAX (CA), and more | 39 |

*Note: Exchange locations indicate registration/headquarters, not service availability

#### Implementation Status (v2.1.12 - 2026-01-12)

| Feature | Implemented | In Progress | Planned |
|---------|------------|-------------|----------|
| WebSocket Clients | 132 | - | - |
| Korean Exchange WebSockets | 6 (Upbit, Bithumb, Coinone, Korbit, Gopax, OKCoinKR) | 1 (Probit) | - |
| Major Exchange Implementations | **16 (100% Complete)** | 1 (MEXC partial) | - |
| Full WebSocket Implementation | **16** | - | 116 |
| Batch Subscription System | **11** | 4 | - |
| Auto-Resubscription on Reconnect | **16** | - | 116 |
| Authentication/Private Channels | - | 16 | - |
| Technical Indicators | 25+ | - | 25+ more |
| Test Coverage | 16 exchanges (439 tests) | - | 116 exchanges |

#### ✅ All 16 Major Exchanges (100% Complete)
Binance, Bitfinex, Bitget, Bithumb, Bitstamp, Bybit, Coinbase, Coinone, Crypto.com, Gate.io, Huobi, Korbit, Kraken, Kucoin, OKX, Upbit - **All functional with standardized WebSocket streaming and batch subscription support**

#### ⚠️ Closed Exchanges
- ~~Bittrex~~ - Exchange closed December 2023

#### 🔒 Security & Testing Status
- **Critical**: Authentication implementation needed for private channels
- **Testing**: All 16 major exchanges have unified WebSocket test suite (439 tests)
- **Channel Management**: Advanced ChannelManager with batch subscription support
- **Observability**: IChannelObserver pattern for metrics and health monitoring
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection support

## 📦 Installation

### NuGet Package Manager
```bash
Install-Package CCXT.Collector -Version 2.1.12
```

### .NET CLI
```bash
dotnet add package CCXT.Collector --version 2.1.12
```

### Package Reference
```xml
<PackageReference Include="CCXT.Collector" Version="2.1.12" />
```

### ⚠️ What's New in v2.1.12
- **New Exchanges**: Kraken, Bitfinex, Bitstamp fully implemented with WebSocket API v2
- **MEXC Exchange**: Partial support (connection works, data parsing requires protobuf)
- **Observability System**: IChannelObserver, ChannelStatistics, ConnectionHealth classes
- **Dependency Injection**: AddCcxtCollector() extension methods for service registration
- **Test Coverage**: Increased from 154 to 439 tests (285% increase)
- **Bitget V2 API**: Migrated from deprecated V1 to V2 WebSocket API
- See [CHANGELOG](docs/releases/v2.md) for full details

### ⚠️ Breaking Changes in v2.1.11
- **Bitget V2 API**: Changed WebSocket URL and subscription format
- See [CHANGELOG](docs/releases/v2.md#2111---2026-01-12) for migration details

### ⚠️ Breaking Changes in v2.1.5
- **IMPORTANT**: Complete migration from Newtonsoft.Json to System.Text.Json
- All JSON processing now uses System.Text.Json for better performance and reduced dependencies
- Added JsonExtensions utility class with safe property access methods
- Unified subscription handling with `MarkSubscriptionActive` across all exchanges
- See [CHANGELOG](docs/releases/v2.md#215---2025-08-11) for migration details

## 🚀 Quick Start

### Basic WebSocket Connection

```csharp
using CCXT.Collector.Binance;
using CCXT.Collector.Service;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Create WebSocket client
        var client = new BinanceWebSocketClient();
        
        // Register callbacks for real-time data
        client.OnOrderbookReceived += (orderbook) =>
        {
            Console.WriteLine($"Orderbook update: {orderbook.symbol}");
            Console.WriteLine($"Best bid: {orderbook.result.bids[0].price} @ {orderbook.result.bids[0].quantity}");
            Console.WriteLine($"Best ask: {orderbook.result.asks[0].price} @ {orderbook.result.asks[0].quantity}");
        };
        
        client.OnConnected += () => Console.WriteLine("✅ Connected to Binance");
        client.OnError += (error) => Console.WriteLine($"❌ Error: {error}");
        
        // Connect and subscribe to markets
        await client.ConnectAsync();
        
        // Using the new Market-based subscription (more efficient)
        var market = new Market("BTC", "USDT");
        await client.SubscribeOrderbookAsync(market);
        await client.SubscribeTradesAsync(market);
        
        // Or using traditional string format (backward compatible)
        await client.SubscribeTickerAsync("BTC/USDT");
        
        // Keep the connection alive
        Console.WriteLine("Press any key to stop...");
        Console.ReadKey();
        
        // Cleanup
        await client.DisconnectAsync();
    }
}
```

### Multi-Exchange Data Collection

```csharp
using CCXT.Collector.Binance;
using CCXT.Collector.Upbit;
using CCXT.Collector.Service;

// Initialize multiple exchanges
var binanceClient = new BinanceWebSocketClient();
var upbitClient = new UpbitWebSocketClient();

// Set up unified callbacks - all exchanges use same data format
Action<STicker> processTicker = (ticker) =>
{
    Console.WriteLine($"[{ticker.exchange}] {ticker.symbol}: " +
                     $"Price={ticker.result.closePrice:F2}, " +
                     $"Volume={ticker.result.volume:F2}");
};

binanceClient.OnTickerReceived += processTicker;
upbitClient.OnTickerReceived += processTicker;

// Connect and subscribe
await Task.WhenAll(
    binanceClient.ConnectAsync(),
    upbitClient.ConnectAsync()
);

// Use Market struct for cleaner code
var btcUsdt = new Market("BTC", "USDT");
var btcKrw = new Market("BTC", "KRW");

await binanceClient.SubscribeTickerAsync(btcUsdt);
await upbitClient.SubscribeTickerAsync(btcKrw);
```

## 📊 Technical Indicators

The library includes 25+ technical indicators. See the consolidated [Contributing Guide](docs/CONTRIBUTING.md#architecture-overview) for an overview and pointers.

## ⚙️ Configuration

```json
{
  "appsettings": {
    "websocket.retry.waiting.milliseconds": "600",
    "use.auto.start": "true",
    "auto.start.exchange.name": "binance",
    "auto.start.symbol.names": "BTC/USDT,ETH/USDT"
  }
}
```

## 🏗️ Architecture

For detailed architecture and system design, see the [Contributing Guide](docs/CONTRIBUTING.md#architecture-overview).

### Project Structure

```
CCXT.Collector/
├── src/
│   ├── Core/             # Core framework components
│   ├── Models/           # Data models and structures
│   ├── Indicators/       # Technical indicators (25+ indicators)
│   ├── Utilities/        # Utility classes
│   └── exchanges/        # Exchange implementations (132 exchanges)
│       ├── kr/           # South Korea (7 exchanges)
│       ├── us/           # United States (26 exchanges)
│       ├── cn/           # China (24 exchanges)
│       └── ...           # 18 more country/region folders
├── tests/                # Test suites
├── samples/              # Example implementations
└── docs/                 # Documentation
```

## 📚 Documentation

- [Contributing Guide](docs/CONTRIBUTING.md) - Architecture, development workflow, deployment and release notes guidelines
- [Roadmap & Tasks](docs/ROADMAP.md) - Development roadmap and current tasks
- [Changelog](docs/releases/README.md) - Version history and release notes

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](docs/CONTRIBUTING.md) for details.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## 🔗 Links

- [NuGet Package](https://www.nuget.org/packages/CCXT.Collector)
- [GitHub Repository](https://github.com/odinsoft-lab/ccxt.collector)
- [Documentation Wiki](https://github.com/odinsoft-lab/ccxt.collector/wiki)
- [Bug Reports](https://github.com/odinsoft-lab/ccxt.collector/issues)

## 👥 Related Projects

- [CCXT.NET](https://github.com/odinsoft-lab/ccxt.net) - The base CCXT library for .NET
- [CCXT.Simple](https://github.com/odinsoft-lab/ccxt.simple) - Simplified exchange interface

## 💬 Support

- **Issues**: [GitHub Issues](https://github.com/odinsoft-lab/ccxt.collector/issues)
- **Email**: support@ccxt.net
- **Discord**: [Join our Discord](https://discord.gg/ccxt)

## 👥 Team

### **Core Development Team**
- **SEONGAHN** - Lead Developer & Project Architect ([lisa@odinsoft.co.kr](mailto:lisa@odinsoft.co.kr))
- **YUJIN** - Senior Developer & Exchange Integration Specialist ([yoojin@odinsoft.co.kr](mailto:yoojin@odinsoft.co.kr))
- **SEJIN** - Software Developer & API Implementation ([saejin@odinsoft.co.kr](mailto:saejin@odinsoft.co.kr))

---

**Built with ❤️ by the ODINSOFT Team** | [⭐ Star us on GitHub](https://github.com/odinsoft-lab/CCXT.Collector)