using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Service;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CCXT.Collector.Tests.Base
{
    /// <summary>
    /// Base class for all WebSocket exchange tests
    /// Provides common testing functionality and assertions
    /// </summary>
    public abstract class WebSocketTestBase : IDisposable
    {
        protected readonly ITestOutputHelper _output;
        protected readonly List<string> _testSymbols;
        protected readonly int _connectionTimeout = 5000; // 5 seconds
        protected int _dataReceiveTimeout = 10000; // 10 seconds (can be overridden)
        protected readonly Dictionary<string, int> _dataCounters;
        protected readonly string _exchangeName;
        private bool _disposed = false;

        // Data collection for validation
        protected readonly Dictionary<string, SOrderBook> _orderbookData = new();
        protected readonly List<STrade> _tradeData = new();
        protected readonly Dictionary<string, STicker> _tickerData = new();
        protected readonly List<string> _errors = new();

        // Connection state tracking
        protected bool _isConnected = false;
        protected bool _hasReceivedData = false;
        protected DateTime _lastDataReceived;
        protected readonly Stopwatch _connectionTimer = new();

        protected WebSocketTestBase(ITestOutputHelper output, string exchangeName)
        {
            _output = output;
            _exchangeName = exchangeName;
            _dataCounters = new Dictionary<string, int>();
            
            // Default test symbols - can be overridden in derived classes
            _testSymbols = GetDefaultTestSymbols();
            
            InitializeCounters();
        }

        protected virtual List<string> GetDefaultTestSymbols()
        {
            return new List<string> { "BTC/USDT", "ETH/USDT" };
        }

        protected void SafeWriteLine(string message)
        {
            if (!_disposed)
            {
                try
                {
                    // Write to xUnit output
                    _output?.WriteLine(message);
                    
                    // Also write to Console for visibility
                    //Console.WriteLine(message);
                    
                    // And to Debug output (visible in IDE)
                    //System.Diagnostics.Debug.WriteLine(message);
                }
                catch
                {
                    // Ignore errors when test is being disposed
                }
            }
        }

        private void InitializeCounters()
        {
            _dataCounters.Clear();
            _dataCounters["orderbook"] = 0;
            _dataCounters["trade"] = 0;
            _dataCounters["ticker"] = 0;
            _dataCounters["error"] = 0;
            _dataCounters["connected"] = 0;
            _dataCounters["disconnected"] = 0;
        }

        #region Abstract Methods - Must be implemented by derived classes

        protected abstract IWebSocketClient CreateClient();
        protected abstract Task<bool> ConnectClientAsync(IWebSocketClient client);
        
        #endregion

        #region Common Test Methods

        /// <summary>
        /// Test basic WebSocket connection
        /// </summary>
        protected async Task TestWebSocketConnection()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing WebSocket Connection");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            var connected = new TaskCompletionSource<bool>();
            
            client.OnConnected += () =>
            {
                _isConnected = true;
                _dataCounters["connected"]++;
                _connectionTimer.Stop();
                connected.TrySetResult(true);
                SafeWriteLine($"✅ Connected to {_exchangeName} in {_connectionTimer.ElapsedMilliseconds}ms");
            };

            client.OnError += (error) =>
            {
                _errors.Add(error);
                _dataCounters["error"]++;
                SafeWriteLine($"❌ Error: {error}");
                connected.TrySetResult(false);
            };

            _connectionTimer.Start();
            var connectTask = ConnectClientAsync(client);
            
            var timeoutTask = Task.Delay(_connectionTimeout);
            var completedTask = await Task.WhenAny(connected.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Connection to {_exchangeName} timed out after {_connectionTimeout}ms");
            }

            var result = await connected.Task;
            Assert.True(result, $"Failed to connect to {_exchangeName}");
            Assert.True(_connectionTimer.ElapsedMilliseconds < _connectionTimeout, 
                $"Connection took too long: {_connectionTimer.ElapsedMilliseconds}ms");
        }

        /// <summary>
        /// Test orderbook data reception
        /// </summary>
        protected async Task TestOrderbookDataReception()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing Orderbook Data Reception");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            var dataReceived = new TaskCompletionSource<bool>();
            
            client.OnOrderbookReceived += (orderbook) =>
            {
                _orderbookData[orderbook.symbol] = orderbook;
                _dataCounters["orderbook"]++;
                _hasReceivedData = true;
                _lastDataReceived = DateTime.UtcNow;
                
                if (_dataCounters["orderbook"] == 1)
                {
                    dataReceived.TrySetResult(true);
                    SafeWriteLine($"✅ First orderbook received for {orderbook.symbol}");
                }
                
                ValidateOrderbook(orderbook);
            };

            client.OnError += (error) =>
            {
                _errors.Add(error);
                SafeWriteLine($"❌ Error: {error}");
            };

            await ConnectClientAsync(client);
            
            // Subscribe to orderbook for test symbols
            foreach (var symbol in _testSymbols)
            {
                await client.SubscribeOrderbookAsync(symbol);
                SafeWriteLine($"📊 Subscribed to {symbol} orderbook");
            }

            // Wait for data or timeout
            var timeoutTask = Task.Delay(_dataReceiveTimeout);
            var completedTask = await Task.WhenAny(dataReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"No orderbook data received from {_exchangeName} after {_dataReceiveTimeout}ms");
            }

            Assert.True(_dataCounters["orderbook"] > 0, "No orderbook data received");
            SafeWriteLine($"✅ Received {_dataCounters["orderbook"]} orderbook updates");
        }

        /// <summary>
        /// Test trade data reception
        /// </summary>
        protected async Task TestTradeDataReception()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing Trade Data Reception");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            var dataReceived = new TaskCompletionSource<bool>();
            
            client.OnTradeReceived += (trade) =>
            {
                _tradeData.Add(trade);
                _dataCounters["trade"]++;
                _hasReceivedData = true;
                _lastDataReceived = DateTime.UtcNow;
                
                if (_dataCounters["trade"] == 1)
                {
                    dataReceived.TrySetResult(true);
                    SafeWriteLine($"✅ First trade received for {trade.symbol}");
                }
                
                ValidateTrade(trade);
            };

            client.OnError += (error) =>
            {
                _errors.Add(error);
                SafeWriteLine($"❌ Error: {error}");
            };

            await ConnectClientAsync(client);
            
            // Subscribe to trades for test symbols
            foreach (var symbol in _testSymbols)
            {
                await client.SubscribeTradesAsync(symbol);
                SafeWriteLine($"📊 Subscribed to {symbol} trades");
            }

            // Wait for data or timeout
            var timeoutTask = Task.Delay(_dataReceiveTimeout);
            var completedTask = await Task.WhenAny(dataReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                SafeWriteLine($"⚠️ Warning: No trade data received from {_exchangeName} after {_dataReceiveTimeout}ms");
                // Some exchanges may not have trades in test period, so we don't throw exception
                return;
            }

            Assert.True(_dataCounters["trade"] > 0, "No trade data received");
            SafeWriteLine($"✅ Received {_dataCounters["trade"]} trades");
        }

        /// <summary>
        /// Test ticker data reception
        /// </summary>
        protected async Task TestTickerDataReception()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing Ticker Data Reception");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            var dataReceived = new TaskCompletionSource<bool>();
            
            client.OnTickerReceived += (ticker) =>
            {
                _tickerData[ticker.symbol] = ticker;
                _dataCounters["ticker"]++;
                _hasReceivedData = true;
                _lastDataReceived = DateTime.UtcNow;
                
                if (_dataCounters["ticker"] == 1)
                {
                    dataReceived.TrySetResult(true);
                    SafeWriteLine($"✅ First ticker received for {ticker.symbol}");
                }
                
                ValidateTicker(ticker);
            };

            client.OnError += (error) =>
            {
                _errors.Add(error);
                SafeWriteLine($"❌ Error: {error}");
            };

            await ConnectClientAsync(client);
            
            // Subscribe to ticker for test symbols
            foreach (var symbol in _testSymbols)
            {
                await client.SubscribeTickerAsync(symbol);
                SafeWriteLine($"📊 Subscribed to {symbol} ticker");
            }

            // Wait for data or timeout
            var timeoutTask = Task.Delay(_dataReceiveTimeout);
            var completedTask = await Task.WhenAny(dataReceived.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"No ticker data received from {_exchangeName} after {_dataReceiveTimeout}ms");
            }

            Assert.True(_dataCounters["ticker"] > 0, "No ticker data received");
            SafeWriteLine($"✅ Received {_dataCounters["ticker"]} ticker updates");
        }

        /// <summary>
        /// Test multiple concurrent subscriptions using new batch pattern
        /// </summary>
        protected async Task TestMultipleSubscriptions()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing Multiple Concurrent Subscriptions (Batch Mode)");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            
            // Setup event handlers
            client.OnOrderbookReceived += (ob) => { lock (_dataCounters) { _dataCounters["orderbook"]++; } };
            client.OnTradeReceived += (t) => { lock (_dataCounters) { _dataCounters["trade"]++; } };
            client.OnTickerReceived += (tk) => { lock (_dataCounters) { _dataCounters["ticker"]++; } };

            // Add all subscriptions before connecting
            foreach (var symbol in _testSymbols)
            {
                client.AddSubscription("orderbook", symbol);
                client.AddSubscription("trades", symbol);
                client.AddSubscription("ticker", symbol);
                SafeWriteLine($"📋 Queued subscriptions for {symbol} (orderbook, trades, ticker)");
            }
            
            SafeWriteLine($"🔄 Connecting and subscribing to {_testSymbols.Count * 3} channels...");
            
            // Connect and subscribe all at once
            var success = await client.ConnectAndSubscribeAsync();
            Assert.True(success, "Failed to connect and subscribe");
            
            SafeWriteLine($"✅ Connected and subscribed to all channels");
            
            // Wait for data
            await Task.Delay(5000);
            
            var totalMessages = _dataCounters["orderbook"] + _dataCounters["trade"] + _dataCounters["ticker"];
            Assert.True(totalMessages > 0, "No data received from multiple subscriptions");
            
            SafeWriteLine($"✅ Total messages received: {totalMessages}");
            SafeWriteLine($"   Orderbooks: {_dataCounters["orderbook"]}");
            SafeWriteLine($"   Trades: {_dataCounters["trade"]}");
            SafeWriteLine($"   Tickers: {_dataCounters["ticker"]}");
        }

        /// <summary>
        /// Test comprehensive multi-market multi-channel subscriptions
        /// </summary>
        protected async Task TestComprehensiveSubscriptions()
        {
            SafeWriteLine($"\n[{_exchangeName}] Testing Comprehensive Multi-Market Multi-Channel Subscriptions");
            SafeWriteLine("----------------------------------------");

            using var client = CreateClient();
            var dataReceived = new Dictionary<string, bool>();
            
            // Setup comprehensive event handlers
            client.OnOrderbookReceived += (ob) => 
            { 
                lock (_dataCounters) 
                { 
                    _dataCounters["orderbook"]++;
                    dataReceived[$"{ob.symbol}_orderbook"] = true;
                }
            };
            
            client.OnTradeReceived += (t) => 
            { 
                lock (_dataCounters) 
                { 
                    _dataCounters["trade"]++;
                    dataReceived[$"{t.symbol}_trade"] = true;
                }
            };
            
            client.OnTickerReceived += (tk) => 
            { 
                lock (_dataCounters) 
                { 
                    _dataCounters["ticker"]++;
                    dataReceived[$"{tk.symbol}_ticker"] = true;
                }
            };

            // Define comprehensive test symbols (more markets)
            var comprehensiveSymbols = GetComprehensiveTestSymbols();
            
            // Add all subscriptions in batch
            var subscriptions = new List<(string channel, string symbol, string interval)>();
            foreach (var symbol in comprehensiveSymbols)
            {
                subscriptions.Add(("orderbook", symbol, null));
                subscriptions.Add(("trades", symbol, null));
                subscriptions.Add(("ticker", symbol, null));
            }
            
            client.AddSubscriptions(subscriptions);
            
            SafeWriteLine($"📋 Queued {subscriptions.Count} subscriptions for {comprehensiveSymbols.Count} markets");
            SafeWriteLine($"🔄 Connecting and subscribing...");
            
            // Connect and subscribe all at once
            var success = await client.ConnectAndSubscribeAsync();
            Assert.True(success, "Failed to connect and subscribe to comprehensive channels");
            
            SafeWriteLine($"✅ Connected and subscribed to {subscriptions.Count} channels");
            
            // Wait for data with longer timeout for comprehensive test
            await Task.Delay(10000);
            
            // Verify we received data from multiple markets
            var marketsWithData = dataReceived.Keys
                .Select(k => k.Split('_')[0])
                .Distinct()
                .Count();
            
            var channelsWithData = dataReceived.Keys
                .Select(k => k.Split('_')[1])
                .Distinct()
                .Count();
            
            SafeWriteLine($"\n📊 Summary:");
            SafeWriteLine($"   Markets with data: {marketsWithData}/{comprehensiveSymbols.Count}");
            SafeWriteLine($"   Channels with data: {channelsWithData}/3");
            SafeWriteLine($"   Total orderbooks: {_dataCounters["orderbook"]}");
            SafeWriteLine($"   Total trades: {_dataCounters["trade"]}");
            SafeWriteLine($"   Total tickers: {_dataCounters["ticker"]}");
            SafeWriteLine($"   Total messages: {_dataCounters["orderbook"] + _dataCounters["trade"] + _dataCounters["ticker"]}");
            
            Assert.True(marketsWithData >= comprehensiveSymbols.Count / 2, 
                $"Insufficient market coverage: {marketsWithData}/{comprehensiveSymbols.Count}");
        }
        
        /// <summary>
        /// Get comprehensive test symbols for extensive testing
        /// </summary>
        protected virtual List<string> GetComprehensiveTestSymbols()
        {
            // Override in derived classes for exchange-specific comprehensive symbols
            return new List<string> 
            { 
                "BTC/USDT", "ETH/USDT", "BNB/USDT", 
                "XRP/USDT", "SOL/USDT", "DOGE/USDT" 
            };
        }

        #endregion

        #region Validation Methods

        protected virtual void ValidateOrderbook(SOrderBook orderbook)
        {
            Assert.NotNull(orderbook);
            Assert.NotNull(orderbook.result);
            Assert.NotEmpty(orderbook.symbol);
            
            if (orderbook.result.bids?.Count > 0)
            {
                // Validate bid prices are descending
                for (int i = 1; i < Math.Min(5, orderbook.result.bids.Count); i++)
                {
                    Assert.True(orderbook.result.bids[i - 1].price >= orderbook.result.bids[i].price,
                        $"Bid prices not in descending order at index {i}");
                }
            }
            
            if (orderbook.result.asks?.Count > 0)
            {
                // Validate ask prices are ascending
                for (int i = 1; i < Math.Min(5, orderbook.result.asks.Count); i++)
                {
                    Assert.True(orderbook.result.asks[i - 1].price <= orderbook.result.asks[i].price,
                        $"Ask prices not in ascending order at index {i}");
                }
            }
            
            // Validate spread if both bids and asks exist
            if (orderbook.result.bids?.Count > 0 && orderbook.result.asks?.Count > 0)
            {
                var spread = orderbook.result.asks[0].price - orderbook.result.bids[0].price;
                Assert.True(spread >= 0, "Invalid spread (best ask < best bid)");
            }
        }

        protected virtual void ValidateTrade(STrade trade)
        {
            Assert.NotNull(trade);
            Assert.NotNull(trade.result);
            Assert.NotEmpty(trade.symbol);
            Assert.True(trade.result.Count > 0, "Trade result is empty");
            
            foreach (var item in trade.result)
            {
                Assert.True(item.price > 0, "Invalid trade price");
                Assert.True(item.quantity > 0, "Invalid trade quantity");
                Assert.NotEmpty(item.tradeId);
            }
        }

        protected virtual void ValidateTicker(STicker ticker)
        {
            Assert.NotNull(ticker);
            Assert.NotNull(ticker.result);
            Assert.NotEmpty(ticker.symbol);
            
            if (ticker.result.bidPrice > 0 && ticker.result.askPrice > 0)
            {
                Assert.True(ticker.result.askPrice >= ticker.result.bidPrice,
                    "Invalid ticker prices (ask < bid)");
            }
        }

        #endregion

        #region Test Summary

        protected void PrintTestSummary()
        {
            SafeWriteLine($"\n[{_exchangeName}] Test Summary");
            SafeWriteLine("========================================");
            SafeWriteLine($"✅ Connections: {_dataCounters["connected"]}");
            SafeWriteLine($"❌ Disconnections: {_dataCounters["disconnected"]}");
            SafeWriteLine($"📊 Orderbook updates: {_dataCounters["orderbook"]}");
            SafeWriteLine($"💰 Trades received: {_dataCounters["trade"]}");
            SafeWriteLine($"📈 Ticker updates: {_dataCounters["ticker"]}");
            SafeWriteLine($"⚠️ Errors: {_dataCounters["error"]}");
            
            if (_errors.Any())
            {
                SafeWriteLine("\nErrors encountered:");
                foreach (var error in _errors.Take(5))
                {
                    SafeWriteLine($"  - {error}");
                }
            }
        }

        #endregion

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                PrintTestSummary();
                _disposed = true;
            }
        }
    }
}