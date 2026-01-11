using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Models.WebSocket;
using CCXT.Collector.Service;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace CCXT.Collector.Kraken
{
    /*
     * Kraken Exchange
     *
     * API Documentation:
     *     https://docs.kraken.com/
     *     https://docs.kraken.com/websockets-v2/
     *
     * WebSocket API v2:
     *     wss://ws.kraken.com/v2 (public)
     *     wss://ws-auth.kraken.com/v2 (private/authenticated)
     *
     * Supported Channels:
     *     - ticker: Level 1 market data (best bid/ask, last trade, 24h stats)
     *     - book: Level 2 order book (aggregated price levels)
     *     - trade: Recent trades stream
     *
     * Symbol Format: BTC/USD, ETH/USD (slash separator)
     *
     * Rate Limits:
     *     - Standard: 200 requests/second
     *     - Pro: 500 requests/second
     *     - Max 200 symbols per connection
     *
     * Features:
     *     - Heartbeat every 1 second
     *     - Snapshot + incremental updates
     *     - CRC32 checksum for order book validation
     */

    /// <summary>
    /// Kraken WebSocket client for real-time market data streaming (API v2)
    /// </summary>
    public class KrakenWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;

        public override string ExchangeName => "Kraken";
        protected override string WebSocketUrl => "wss://ws.kraken.com/v2";
        protected override string PrivateWebSocketUrl => "wss://ws-auth.kraken.com/v2";
        protected override int PingIntervalMs => 30000; // 30 seconds (Kraken sends heartbeat every 1s)

        public KrakenWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
        }

        /// <summary>
        /// Kraken uses standard symbol format (BTC/USD) - no conversion needed
        /// </summary>
        protected override string FormatSymbol(Market market)
        {
            return $"{market.Base}/{market.Quote}";
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // Handle heartbeat messages
                if (json.TryGetProperty("channel", out var channelProp))
                {
                    var channel = channelProp.GetString();

                    if (channel == "heartbeat")
                    {
                        // Heartbeat received, connection is alive
                        return;
                    }

                    // Handle data channels
                    if (json.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    {
                        switch (channel)
                        {
                            case "ticker":
                                await ProcessTicker(dataProp);
                                break;
                            case "book":
                                await ProcessOrderbook(json, dataProp);
                                break;
                            case "trade":
                                await ProcessTrades(dataProp);
                                break;
                        }
                    }
                }

                // Handle subscription responses
                if (json.TryGetProperty("method", out var methodProp))
                {
                    var method = methodProp.GetString();

                    if (method == "subscribe" && json.TryGetProperty("success", out var successProp))
                    {
                        var success = successProp.GetBoolean();
                        if (!success && json.TryGetProperty("error", out var errorProp))
                        {
                            RaiseError($"Kraken subscription error: {errorProp.GetString()}");
                        }
                    }
                }

                // Handle status messages
                if (json.TryGetProperty("channel", out var statusChannel) && statusChannel.GetString() == "status")
                {
                    if (json.TryGetProperty("data", out var statusData) && statusData.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in statusData.EnumerateArray())
                        {
                            var system = item.GetStringOrDefault("system");
                            var connectionId = item.GetStringOrDefault("connection_id");
                            RaiseInfo($"Kraken status: system={system}, connection_id={connectionId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing message: {ex.Message}");
            }
        }

        private async Task ProcessTicker(JsonElement data)
        {
            try
            {
                foreach (var item in data.EnumerateArray())
                {
                    var symbol = item.GetStringOrDefault("symbol");
                    if (string.IsNullOrEmpty(symbol))
                        continue;

                    var timestamp = TimeExtension.UnixTime;

                    var ticker = new STicker
                    {
                        exchange = ExchangeName,
                        symbol = symbol,
                        timestamp = timestamp,
                        result = new STickerItem
                        {
                            timestamp = timestamp,
                            bidPrice = item.GetDecimalOrDefault("bid"),
                            bidQuantity = item.GetDecimalOrDefault("bid_qty"),
                            askPrice = item.GetDecimalOrDefault("ask"),
                            askQuantity = item.GetDecimalOrDefault("ask_qty"),
                            closePrice = item.GetDecimalOrDefault("last"),
                            volume = item.GetDecimalOrDefault("volume"),
                            vwap = item.GetDecimalOrDefault("vwap"),
                            lowPrice = item.GetDecimalOrDefault("low"),
                            highPrice = item.GetDecimalOrDefault("high"),
                            change = item.GetDecimalOrDefault("change"),
                            percentage = item.GetDecimalOrDefault("change_pct")
                        }
                    };

                    InvokeTickerCallback(ticker);
                    RecordMessageMetrics("ticker", symbol, 0, 0);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing ticker: {ex.Message}");
            }
        }

        private async Task ProcessOrderbook(JsonElement json, JsonElement data)
        {
            try
            {
                var type = json.GetStringOrDefault("type"); // "snapshot" or "update"

                foreach (var item in data.EnumerateArray())
                {
                    var symbol = item.GetStringOrDefault("symbol");
                    if (string.IsNullOrEmpty(symbol))
                        continue;

                    var timestamp = TimeExtension.UnixTime;

                    // For snapshot, create new orderbook
                    if (type == "snapshot" || !_orderbookCache.ContainsKey(symbol))
                    {
                        var orderbook = new SOrderBook
                        {
                            exchange = ExchangeName,
                            symbol = symbol,
                            timestamp = timestamp,
                            result = new SOrderBookData
                            {
                                timestamp = timestamp,
                                bids = new List<SOrderBookItem>(),
                                asks = new List<SOrderBookItem>()
                            }
                        };

                        // Process bids
                        if (item.TryGetArray("bids", out var bids))
                        {
                            foreach (var bid in bids.EnumerateArray())
                            {
                                orderbook.result.bids.Add(new SOrderBookItem
                                {
                                    price = bid.GetDecimalOrDefault("price"),
                                    quantity = bid.GetDecimalOrDefault("qty")
                                });
                            }
                        }

                        // Process asks
                        if (item.TryGetArray("asks", out var asks))
                        {
                            foreach (var ask in asks.EnumerateArray())
                            {
                                orderbook.result.asks.Add(new SOrderBookItem
                                {
                                    price = ask.GetDecimalOrDefault("price"),
                                    quantity = ask.GetDecimalOrDefault("qty")
                                });
                            }
                        }

                        _orderbookCache[symbol] = orderbook;
                        InvokeOrderbookCallback(orderbook);
                        RecordMessageMetrics("orderbook", symbol, 0, 0);
                    }
                    else
                    {
                        // For update, modify existing orderbook
                        var orderbook = _orderbookCache[symbol];
                        orderbook.timestamp = timestamp;
                        orderbook.result.timestamp = timestamp;

                        // Apply bid updates
                        if (item.TryGetArray("bids", out var bidUpdates))
                        {
                            foreach (var bid in bidUpdates.EnumerateArray())
                            {
                                var price = bid.GetDecimalOrDefault("price");
                                var qty = bid.GetDecimalOrDefault("qty");
                                ApplyOrderbookUpdate(orderbook.result.bids, price, qty, true);
                            }
                        }

                        // Apply ask updates
                        if (item.TryGetArray("asks", out var askUpdates))
                        {
                            foreach (var ask in askUpdates.EnumerateArray())
                            {
                                var price = ask.GetDecimalOrDefault("price");
                                var qty = ask.GetDecimalOrDefault("qty");
                                ApplyOrderbookUpdate(orderbook.result.asks, price, qty, false);
                            }
                        }

                        InvokeOrderbookCallback(orderbook);
                        RecordMessageMetrics("orderbook", symbol, 0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing orderbook: {ex.Message}");
            }
        }

        private void ApplyOrderbookUpdate(List<SOrderBookItem> levels, decimal price, decimal qty, bool isBid)
        {
            // Remove if qty is 0
            if (qty == 0)
            {
                levels.RemoveAll(l => l.price == price);
                return;
            }

            // Find and update existing level or add new one
            var existing = levels.Find(l => l.price == price);
            if (existing != null)
            {
                existing.quantity = qty;
            }
            else
            {
                levels.Add(new SOrderBookItem { price = price, quantity = qty });

                // Sort: bids descending, asks ascending
                if (isBid)
                    levels.Sort((a, b) => b.price.CompareTo(a.price));
                else
                    levels.Sort((a, b) => a.price.CompareTo(b.price));
            }
        }

        private async Task ProcessTrades(JsonElement data)
        {
            try
            {
                // Group trades by symbol
                var tradesBySymbol = new Dictionary<string, List<STradeItem>>();

                foreach (var item in data.EnumerateArray())
                {
                    var symbol = item.GetStringOrDefault("symbol");
                    if (string.IsNullOrEmpty(symbol))
                        continue;

                    if (!tradesBySymbol.ContainsKey(symbol))
                        tradesBySymbol[symbol] = new List<STradeItem>();

                    var side = item.GetStringOrDefault("side");
                    var timestampStr = item.GetStringOrDefault("timestamp");
                    var timestamp = TimeExtension.UnixTime;

                    if (!string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, out var dt))
                    {
                        timestamp = new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeMilliseconds();
                    }

                    tradesBySymbol[symbol].Add(new STradeItem
                    {
                        tradeId = item.GetInt64OrDefault("trade_id").ToString(),
                        sideType = side == "buy" ? SideType.Bid : SideType.Ask,
                        orderType = item.GetStringOrDefault("ord_type") == "market" ? OrderType.Market : OrderType.Limit,
                        price = item.GetDecimalOrDefault("price"),
                        quantity = item.GetDecimalOrDefault("qty"),
                        amount = item.GetDecimalOrDefault("price") * item.GetDecimalOrDefault("qty"),
                        timestamp = timestamp
                    });
                }

                // Invoke callbacks for each symbol
                foreach (var kvp in tradesBySymbol)
                {
                    var trade = new STrade
                    {
                        exchange = ExchangeName,
                        symbol = kvp.Key,
                        timestamp = TimeExtension.UnixTime,
                        result = kvp.Value
                    };

                    InvokeTradeCallback(trade);
                    RecordMessageMetrics("trades", kvp.Key, 0, 0);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing trades: {ex.Message}");
            }
        }

        #region Subscription Methods

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var subscribeMessage = new
                {
                    method = "subscribe",
                    @params = new
                    {
                        channel = "ticker",
                        symbol = new[] { symbol },
                        snapshot = true
                    }
                };

                var json = JsonSerializer.Serialize(subscribeMessage);
                await SendMessageAsync(json);
                MarkSubscriptionActive("ticker", symbol);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Error subscribing to ticker: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeOrderbookAsync(string symbol)
        {
            try
            {
                var subscribeMessage = new
                {
                    method = "subscribe",
                    @params = new
                    {
                        channel = "book",
                        symbol = new[] { symbol },
                        depth = 25,
                        snapshot = true
                    }
                };

                var json = JsonSerializer.Serialize(subscribeMessage);
                await SendMessageAsync(json);
                MarkSubscriptionActive("orderbook", symbol);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Error subscribing to orderbook: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeTradesAsync(string symbol)
        {
            try
            {
                var subscribeMessage = new
                {
                    method = "subscribe",
                    @params = new
                    {
                        channel = "trade",
                        symbol = new[] { symbol },
                        snapshot = true
                    }
                };

                var json = JsonSerializer.Serialize(subscribeMessage);
                await SendMessageAsync(json);
                MarkSubscriptionActive("trades", symbol);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Error subscribing to trades: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeCandlesAsync(string symbol, string interval)
        {
            // Kraken WebSocket v2 doesn't support OHLC/candles channel
            // Use REST API for historical OHLC data
            RaiseInfo("Kraken WebSocket v2 doesn't support candles. Use REST API for OHLC data.");
            return false;
        }

        public override async Task<bool> UnsubscribeAsync(string channel, string symbol)
        {
            try
            {
                var krakenChannel = channel.ToLower() switch
                {
                    "orderbook" or "depth" => "book",
                    "trades" or "trade" => "trade",
                    _ => channel
                };

                var unsubscribeMessage = new
                {
                    method = "unsubscribe",
                    @params = new
                    {
                        channel = krakenChannel,
                        symbol = new[] { symbol }
                    }
                };

                var json = JsonSerializer.Serialize(unsubscribeMessage);
                await SendMessageAsync(json);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Error unsubscribing: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Batch Subscription Support

        protected override bool SupportsBatchSubscription()
        {
            return true;
        }

        protected override async Task<bool> SendBatchSubscriptionsAsync(List<KeyValuePair<string, SubscriptionInfo>> subscriptions)
        {
            try
            {
                // Group by channel
                var tickerSymbols = new List<string>();
                var bookSymbols = new List<string>();
                var tradeSymbols = new List<string>();

                foreach (var kvp in subscriptions)
                {
                    var subscription = kvp.Value;
                    var channel = subscription.Channel.ToLower();

                    switch (channel)
                    {
                        case "ticker":
                            tickerSymbols.Add(subscription.Symbol);
                            break;
                        case "orderbook":
                        case "depth":
                            bookSymbols.Add(subscription.Symbol);
                            break;
                        case "trades":
                        case "trade":
                            tradeSymbols.Add(subscription.Symbol);
                            break;
                    }
                }

                // Send batch subscriptions per channel
                if (tickerSymbols.Count > 0)
                {
                    var msg = new
                    {
                        method = "subscribe",
                        @params = new
                        {
                            channel = "ticker",
                            symbol = tickerSymbols.ToArray(),
                            snapshot = true
                        }
                    };
                    await SendMessageAsync(JsonSerializer.Serialize(msg));
                    RaiseInfo($"Subscribed to {tickerSymbols.Count} ticker symbols");
                }

                if (bookSymbols.Count > 0)
                {
                    var msg = new
                    {
                        method = "subscribe",
                        @params = new
                        {
                            channel = "book",
                            symbol = bookSymbols.ToArray(),
                            depth = 25,
                            snapshot = true
                        }
                    };
                    await SendMessageAsync(JsonSerializer.Serialize(msg));
                    RaiseInfo($"Subscribed to {bookSymbols.Count} orderbook symbols");
                }

                if (tradeSymbols.Count > 0)
                {
                    var msg = new
                    {
                        method = "subscribe",
                        @params = new
                        {
                            channel = "trade",
                            symbol = tradeSymbols.ToArray(),
                            snapshot = true
                        }
                    };
                    await SendMessageAsync(JsonSerializer.Serialize(msg));
                    RaiseInfo($"Subscribed to {tradeSymbols.Count} trade symbols");
                }

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Batch subscription failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        protected override string CreatePingMessage()
        {
            // Kraken v2 uses heartbeat channel, but we can send a ping
            return JsonSerializer.Serialize(new { method = "ping" });
        }
    }
}
