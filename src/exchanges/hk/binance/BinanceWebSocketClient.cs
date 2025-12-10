using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Service;
using System.Text.Json;
using CCXT.Collector.Models.WebSocket;
using CCXT.Collector.Core.Infrastructure;

namespace CCXT.Collector.Binance
{
    /*
     * Binance Support Markets: USDT, BUSD, BTC, ETH, BNB
     *
     * API Documentation:
     *     https://binance-docs.github.io/apidocs/
     *     https://github.com/binance/binance-spot-api-docs
     *
     * WebSocket API:
     *     https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/general-api-information
     *     https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/request-format
     *     https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/response-format
     *     https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/market-data-requests
     *     https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/trading-requests
     *
     * Fees:
     *     https://www.binance.com/en/fee/schedule
     */

    /// <summary>
    /// Binance WebSocket client for real-time data streaming
    /// </summary>
    public class BinanceWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, long> _lastUpdateIds;
        private readonly Dictionary<string, SOrderBook> _orderbookCache;
        //private string _listenKey; // For user data stream

        public override string ExchangeName => "Binance";
        protected override string WebSocketUrl => "wss://stream.binance.com:9443/ws";
        protected override string PrivateWebSocketUrl => "wss://stream.binance.com:9443/ws";
        // Binance uses WebSocket protocol-level keepalive + server-initiated ping
        // Client should respond to server's ping with pong, not send custom ping messages
        protected override int PingIntervalMs => 0; // Disabled - rely on WebSocket KeepAlive and server ping/pong

        public BinanceWebSocketClient()
        {
            _lastUpdateIds = new Dictionary<string, long>();
            _orderbookCache = new Dictionary<string, SOrderBook>();
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // Note: Binance uses WebSocket protocol-level ping/pong frames (not JSON messages)
                // .NET ClientWebSocket automatically responds to ping frames with pong frames

                // Handle different message types
                if (json.TryGetProperty("e", out var eProp))
                {
                    var eventType = json.GetStringOrDefault("e");

                    if (isPrivate)
                    {
                        // Private/User data stream events
                        switch (eventType)
                        {
                            case "outboundAccountPosition":
                                await ProcessAccountUpdate(json);
                                break;
                            case "balanceUpdate":
                                await ProcessBalanceUpdate(json);
                                break;
                            case "executionReport":
                                await ProcessOrderUpdate(json);
                                break;
                            case "listStatus":
                                // OCO order status
                                break;
                        }
                    }
                    else
                    {
                        // Public stream events
                        switch (eventType)
                        {
                            case "depthUpdate":
                                await ProcessOrderbookUpdate(json);
                                break;
                            case "trade":
                                await ProcessTradeData(json);
                                break;
                            case "24hrTicker":
                                await ProcessTickerData(json);
                                break;
                            case "kline":
                                await ProcessKlineData(json);
                                break;
                            case "error":
                                var errorMessage = json.GetStringOrDefault("m", "Unknown error");
                                RaiseError($"Binance error: {errorMessage}");
                                break;
                        }
                    }
                }
                else if (!json.TryGetProperty("result", out _) && json.TryGetProperty("id", out var idProp))
                {
                    // Response to subscription request
                    // Binance doesn't send explicit subscription confirmations
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Message processing error: {ex.Message}");
            }
        }

        private async Task ProcessOrderbookUpdate(JsonElement json)
        {
            try
            {
                var symbol = ConvertSymbol(json.GetStringOrDefault("s"));
                var updateId = json.GetInt64OrDefault("u");

                // Check if we should process this update
                if (_lastUpdateIds.ContainsKey(symbol))
                {
                    if (updateId <= _lastUpdateIds[symbol])
                        return;
                }

                _lastUpdateIds[symbol] = updateId;

                var orderbook = new SOrderBook
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = json.GetInt64OrDefault("E"),
                    sequentialId = updateId,
                    result = new SOrderBookData
                    {
                        timestamp = json.GetInt64OrDefault("E"),
                        asks = new List<SOrderBookItem>(),
                        bids = new List<SOrderBookItem>()
                    }
                };

                // Process bids
                if (json.TryGetArray("b", out var bids))
                {
                    foreach (var bid in bids.EnumerateArray())
                    {
                        if (bid.GetArrayLength() < 4)
                            continue;

                        var price = bid[0].GetDecimalValue();
                        var quantity = bid[1].GetDecimalValue();
                        var orders = bid[3].GetInt32Value();

                        if (quantity > 0)
                        {
                            orderbook.result.bids.Add(new SOrderBookItem
                            {
                                price = price,
                                quantity = quantity,
                                amount = price * quantity,
                                action = "U"  // Update
                            });
                        }
                    }
                }

                // Process asks
                if (json.TryGetArray("a", out var asks))
                {
                    foreach (var ask in asks.EnumerateArray())
                    {
                        if (ask.GetArrayLength() < 2)
                            continue;

                        var price = ask[0].GetDecimalValue();
                        var quantity = ask[1].GetDecimalValue();

                        if (quantity > 0)
                        {
                            orderbook.result.asks.Add(new SOrderBookItem
                            {
                                price = price,
                                quantity = quantity,
                                amount = price * quantity,
                                action = "U"  // Update
                            });
                        }
                    }
                }

                // Sort orderbook
                orderbook.result.bids = orderbook.result.bids.OrderByDescending(b => b.price).ToList();
                orderbook.result.asks = orderbook.result.asks.OrderBy(a => a.price).ToList();

                InvokeOrderbookCallback(orderbook);
            }
            catch (Exception ex)
            {
                RaiseError($"Orderbook processing error: {ex.Message}");
            }
        }

        private async Task ProcessTradeData(JsonElement json)
        {
            try
            {
                var trade = new STrade
                {
                    exchange = ExchangeName,
                    symbol = ConvertSymbol(json.GetStringOrDefault("s")),
                    timestamp = json.GetInt64OrDefault("E"),
                    result = new List<STradeItem>
                    {
                        new STradeItem
                        {
                            tradeId = json.GetStringOrDefault("t"),
                            timestamp = json.GetInt64OrDefault("T"),
                            sideType = json.GetBooleanOrFalse("m") ? SideType.Ask : SideType.Bid,
                            orderType = OrderType.Limit,
                            price = json.GetDecimalOrDefault("p"),
                            quantity = json.GetDecimalOrDefault("q"),
                            amount = json.GetDecimalOrDefault("p") * json.GetDecimalOrDefault("q")
                        }
                    }
                };

                InvokeTradeCallback(trade);
            }
            catch (Exception ex)
            {
                RaiseError($"Trade processing error: {ex.Message}");
            }
        }

        private async Task ProcessTickerData(JsonElement json)
        {
            try
            {
                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = ConvertSymbol(json.GetStringOrDefault("s")),
                    timestamp = json.GetInt64OrDefault("E"),
                    result = new STickerItem
                    {
                        timestamp = json.GetInt64OrDefault("E"),
                        openPrice = json.GetDecimalOrDefault("o"),
                        highPrice = json.GetDecimalOrDefault("h"),
                        lowPrice = json.GetDecimalOrDefault("l"),
                        closePrice = json.GetDecimalOrDefault("c"),
                        volume = json.GetDecimalOrDefault("v"),
                        quoteVolume = json.GetDecimalOrDefault("q"),
                        bidPrice = json.GetDecimalOrDefault("b"),
                        bidQuantity = json.GetDecimalOrDefault("B"),
                        askPrice = json.GetDecimalOrDefault("a"),
                        askQuantity = json.GetDecimalOrDefault("A"),
                        vwap = json.GetDecimalOrDefault("w"),
                        count = json.GetInt64OrDefault("C"),
                        change = json.GetDecimalOrDefault("c") - json.GetDecimalOrDefault("o"),
                        percentage = json.GetDecimalOrDefault("o") > 0
                            ? ((json.GetDecimalOrDefault("c") - json.GetDecimalOrDefault("o")) / json.GetDecimalOrDefault("o")) * 100
                            : 0
                    }
                };

                InvokeTickerCallback(ticker);
            }
            catch (Exception ex)
            {
                RaiseError($"Ticker processing error: {ex.Message}");
            }
        }

        public override async Task<bool> SubscribeOrderbookAsync(string symbol)
        {
            try
            {
                var binanceSymbol = ConvertToBinanceSymbol(symbol);
                var streamName = $"{binanceSymbol.ToLower()}@depth@100ms";

                var subscription = new
                {
                    method = "SUBSCRIBE",
                    @params = new[] { streamName },
                    id = DateTime.UtcNow.Ticks
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                MarkSubscriptionActive("orderbook", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Subscribe orderbook error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeTradesAsync(string symbol)
        {
            try
            {
                var binanceSymbol = ConvertToBinanceSymbol(symbol);
                var streamName = $"{binanceSymbol.ToLower()}@trade";

                var subscription = new
                {
                    method = "SUBSCRIBE",
                    @params = new[] { streamName },
                    id = DateTime.UtcNow.Ticks
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                MarkSubscriptionActive("trades", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Subscribe trades error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var binanceSymbol = ConvertToBinanceSymbol(symbol);
                var streamName = $"{binanceSymbol.ToLower()}@ticker";

                var subscription = new
                {
                    method = "SUBSCRIBE",
                    @params = new[] { streamName },
                    id = DateTime.UtcNow.Ticks
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                MarkSubscriptionActive("ticker", symbol);

                RaiseInfo($"Subscribed to 1 ticker(s): {binanceSymbol}");

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Subscribe ticker error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> UnsubscribeAsync(string channel, string symbol)
        {
            try
            {
                var binanceSymbol = ConvertToBinanceSymbol(symbol);
                var streamName = channel switch
                {
                    "orderbook" => $"{binanceSymbol.ToLower()}@depth@100ms",
                    "trades" => $"{binanceSymbol.ToLower()}@trade",
                    "ticker" => $"{binanceSymbol.ToLower()}@ticker",
                    _ => throw new ArgumentException($"Unknown channel: {channel}")
                };

                var unsubscription = new
                {
                    method = "UNSUBSCRIBE",
                    @params = new[] { streamName },
                    id = DateTime.UtcNow.Ticks
                };

                await SendMessageAsync(JsonSerializer.Serialize(unsubscription));

                var key = CreateSubscriptionKey(channel, symbol);
                if (_subscriptions.TryRemove(key, out var sub))
                {
                    sub.IsActive = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Unsubscribe error: {ex.Message}");
                return false;
            }
        }

        protected override string CreatePingMessage()
        {
            // Binance doesn't require client-initiated ping messages
            // Server sends ping and client responds with pong (handled in ProcessMessageAsync)
            return null;
        }

        protected override async Task ResubscribeAsync(SubscriptionInfo subscription)
        {
            switch (subscription.Channel)
            {
                case "orderbook":
                    await SubscribeOrderbookAsync(subscription.Symbol);
                    break;
                case "trades":
                    await SubscribeTradesAsync(subscription.Symbol);
                    break;
                case "ticker":
                    await SubscribeTickerAsync(subscription.Symbol);
                    break;
            }
        }

        #region Helper Methods

        private string ConvertToBinanceSymbol(string symbol) => ParsingHelpers.RemoveDelimiter(symbol);
        private string ConvertSymbol(string binanceSymbol) => ParsingHelpers.NormalizeSymbol(binanceSymbol);

        #endregion

        #region Candlestick/K-Line Implementation

        public override async Task<bool> SubscribeCandlesAsync(string symbol, string interval)
        {
            try
            {
                var binanceSymbol = ConvertToBinanceSymbol(symbol);
                var binanceInterval = ConvertToBinanceInterval(interval);
                var streamName = $"{binanceSymbol.ToLower()}@kline_{binanceInterval}";

                var subscription = new
                {
                    method = "SUBSCRIBE",
                    @params = new[] { streamName },
                    id = DateTime.UtcNow.Ticks
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));

                MarkSubscriptionActive("kline", symbol, interval);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Subscribe klines error: {ex.Message}");
                return false;
            }
        }

        private async Task ProcessKlineData(JsonElement json)
        {
            try
            {
                if (!json.TryGetProperty("k", out var klineData))
                    return;

                var symbol = ConvertSymbol(json.GetStringOrDefault("s"));
                var timestamp = json.GetInt64OrDefault("E");

                var candle = new SCandle
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    interval = ConvertFromBinanceInterval(klineData.GetStringOrDefault("i")),
                    timestamp = timestamp,
                    result = new List<SCandleItem>
                    {
                        new SCandleItem
                        {
                            openTime = klineData.GetInt64OrDefault("t"),
                            closeTime = klineData.GetInt64OrDefault("T"),
                            open = klineData.GetDecimalOrDefault("o"),
                            high = klineData.GetDecimalOrDefault("h"),
                            low = klineData.GetDecimalOrDefault("l"),
                            close = klineData.GetDecimalOrDefault("c"),
                            volume = klineData.GetDecimalOrDefault("v"),
                            quoteVolume = klineData.GetDecimalOrDefault("q"),
                            tradeCount = klineData.GetInt64OrDefault("n"),
                            isClosed = klineData.GetBooleanOrFalse("x"),
                            buyVolume = klineData.GetDecimalOrDefault("V"),
                            buyQuoteVolume = klineData.GetDecimalOrDefault("Q")
                        }
                    }
                };

                InvokeCandleCallback(candle);
            }
            catch (Exception ex)
            {
                RaiseError($"Kline processing error: {ex.Message}");
            }
        }

        private string ConvertToBinanceInterval(string interval) => ParsingHelpers.ToBinanceInterval(interval);
        private string ConvertFromBinanceInterval(string interval) => ParsingHelpers.FromBinanceInterval(interval);

        #endregion

        #region Private Channel Implementation

        protected override bool RequiresSeparatePrivateConnection() => true;

        protected override string CreateAuthenticationMessage(string apiKey, string secretKey)
        {
            // Binance uses a REST API call to get a listenKey for user data stream
            // This is a placeholder - real implementation would call REST API
            // POST /api/v3/userDataStream to get listenKey
            //_listenKey = "placeholder_listen_key";
            return null; // Binance doesn't send auth message, uses listenKey in URL
        }

        public override async Task<bool> SubscribeBalanceAsync()
        {
            if (!IsAuthenticated)
            {
                RaiseError("Not authenticated. Please connect with API credentials.");
                return false;
            }

            // Binance user data stream automatically includes balance updates
            // when connected with listenKey
            return true;
        }

        public override async Task<bool> SubscribeOrdersAsync()
        {
            if (!IsAuthenticated)
            {
                RaiseError("Not authenticated. Please connect with API credentials.");
                return false;
            }

            // Binance user data stream automatically includes order updates
            // when connected with listenKey
            return true;
        }

        private async Task ProcessAccountUpdate(JsonElement json)
        {
            try
            {
                if (json.TryGetArray("B", out var balances))
                {
                    var balanceItems = new List<SBalanceItem>();
                    var timestamp = json.GetInt64OrDefault("E");

                    foreach (var item in balances.EnumerateArray())
                    {
                        var free = item.GetDecimalOrDefault("f");
                        var locked = item.GetDecimalOrDefault("l");

                        if (free > 0 || locked > 0)
                        {
                            balanceItems.Add(new SBalanceItem
                            {
                                currency = item.GetStringOrDefault("a"),
                                free = free,
                                used = locked,
                                total = free + locked,
                                updateTime = timestamp
                            });
                        }
                    }

                    if (balanceItems.Count > 0)
                    {
                        var balance = new SBalance
                        {
                            exchange = ExchangeName,
                            accountId = "spot",
                            timestamp = timestamp,
                            balances = balanceItems
                        };

                        InvokeBalanceCallback(balance);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Account update processing error: {ex.Message}");
            }
        }

        private async Task ProcessBalanceUpdate(JsonElement json)
        {
            try
            {
                // Single balance update
                var balance = new SBalance
                {
                    exchange = ExchangeName,
                    accountId = "spot",
                    timestamp = json.GetInt64OrDefault("E"),
                    balances = new List<SBalanceItem>
                    {
                        new SBalanceItem
                        {
                            currency = json.GetStringOrDefault("a"),
                            free = json.GetDecimalOrDefault("f"),
                            used = json.GetDecimalOrDefault("l"),
                            total = json.GetDecimalOrDefault("f") + json.GetDecimalOrDefault("l"),
                            updateTime = json.GetInt64OrDefault("E")
                        }
                    }
                };

                InvokeBalanceCallback(balance);
            }
            catch (Exception ex)
            {
                RaiseError($"Balance update processing error: {ex.Message}");
            }
        }

        private async Task ProcessOrderUpdate(JsonElement json)
        {
            try
            {
                var order = new SOrderItem
                {
                    orderId = json.GetStringOrDefault("i"),
                    clientOrderId = json.GetStringOrDefault("c"),
                    symbol = ConvertSymbol(json.GetStringOrDefault("s")),
                    type = ParseOrderType(json.GetStringOrDefault("o")),
                    side = json.GetStringOrDefault("S") == "BUY" ? OrderSide.Buy : OrderSide.Sell,
                    status = ParseOrderStatus(json.GetStringOrDefault("X")),
                    price = json.GetDecimalOrDefault("p"),
                    quantity = json.GetDecimalOrDefault("q"),
                    filledQuantity = json.GetDecimalOrDefault("z"),
                    createTime = json.GetInt64OrDefault("O"),
                    updateTime = json.GetInt64OrDefault("E")
                };

                var orders = new SOrder
                {
                    exchange = ExchangeName,
                    timestamp = json.GetInt64OrDefault("E"),
                    orders = new List<SOrderItem> { order }
                };

                InvokeOrderCallback(orders);
            }
            catch (Exception ex)
            {
                RaiseError($"Order update processing error: {ex.Message}");
            }
        }

        private OrderType ParseOrderType(string type) => (OrderType)CCXT.Collector.Core.Infrastructure.ParsingHelpers.ParseGenericOrderType(type);
        private OrderStatus ParseOrderStatus(string status) => (OrderStatus)CCXT.Collector.Core.Infrastructure.ParsingHelpers.ParseGenericOrderStatus(status);

        #endregion

        #region Batch Subscription Support

        /// <summary>
        /// Binance supports batch subscription - multiple streams in params array
        /// </summary>
        protected override bool SupportsBatchSubscription()
        {
            return true;
        }

        /// <summary>
        /// Send batch subscriptions for Binance - combines multiple streams in single message
        /// </summary>
        protected override async Task<bool> SendBatchSubscriptionsAsync(List<KeyValuePair<string, SubscriptionInfo>> subscriptions)
        {
            try
            {
                // Build list of all stream names
                var streamNames = new List<string>();

                foreach (var kvp in subscriptions)
                {
                    var subscription = kvp.Value;
                    var binanceSymbol = ConvertToBinanceSymbol(subscription.Symbol).ToLower();

                    // Map channel names to Binance stream format
                    string streamName = subscription.Channel.ToLower() switch
                    {
                        "orderbook" or "depth" => $"{binanceSymbol}@depth@100ms",
                        "trades" or "trade" => $"{binanceSymbol}@trade",
                        "ticker" => $"{binanceSymbol}@ticker",
                        "candles" or "kline" or "candlestick" => !string.IsNullOrEmpty(subscription.Extra)
                            ? $"{binanceSymbol}@kline_{ConvertToBinanceInterval(subscription.Extra)}"
                            : $"{binanceSymbol}@kline_1h",
                        "aggTrade" => $"{binanceSymbol}@aggTrade",
                        "miniTicker" => $"{binanceSymbol}@miniTicker",
                        "bookTicker" => $"{binanceSymbol}@bookTicker",
                        _ => $"{binanceSymbol}@{subscription.Channel}"
                    };

                    streamNames.Add(streamName);
                }

                if (streamNames.Count == 0)
                    return true;

                // Binance allows multiple streams in a single subscription message
                // Limit to 200 streams per connection (Binance's typical limit)
                const int maxStreamsPerMessage = 200;

                if (streamNames.Count <= maxStreamsPerMessage)
                {
                    // Send all streams in one message
                    var subscriptionMessage = new
                    {
                        method = "SUBSCRIBE",
                        @params = streamNames.ToArray(),
                        id = DateTime.UtcNow.Ticks
                    };

                    await SendMessageAsync(JsonSerializer.Serialize(subscriptionMessage));
                    RaiseInfo($"Sent Binance batch subscription with {streamNames.Count} streams");
                }
                else
                {
                    // Split into multiple messages if exceeding limit
                    var messageCount = (streamNames.Count + maxStreamsPerMessage - 1) / maxStreamsPerMessage;

                    for (int i = 0; i < messageCount; i++)
                    {
                        var batch = streamNames
                            .Skip(i * maxStreamsPerMessage)
                            .Take(maxStreamsPerMessage)
                            .ToArray();

                        var subscriptionMessage = new
                        {
                            method = "SUBSCRIBE",
                            @params = batch,
                            id = DateTime.UtcNow.Ticks + i
                        };

                        await SendMessageAsync(JsonSerializer.Serialize(subscriptionMessage));
                        RaiseInfo($"Sent Binance batch subscription {i + 1}/{messageCount} with {batch.Length} streams");

                        // Small delay between batches if multiple messages
                        if (i < messageCount - 1)
                            await Task.Delay(100);
                    }
                }

                RaiseInfo($"Completed Binance batch subscription for {subscriptions.Count} total subscriptions");

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Batch subscription failed: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
