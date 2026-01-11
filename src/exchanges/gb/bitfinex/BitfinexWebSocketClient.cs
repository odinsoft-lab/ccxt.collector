using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Service;
using System.Text.Json;
using CCXT.Collector.Models.WebSocket;

namespace CCXT.Collector.Bitfinex
{
    /*
     * Bitfinex Exchange
     *
     * API Documentation:
     *     https://docs.bitfinex.com/docs
     *     https://docs.bitfinex.com/docs/ws-general
     *
     * WebSocket API v2:
     *     wss://api-pub.bitfinex.com/ws/2 (public)
     *     wss://api.bitfinex.com/ws/2 (private/authenticated)
     *
     * Supported Channels:
     *     - ticker: Real-time ticker updates
     *     - book: Order book with configurable precision and length
     *     - trades: Real-time trade stream
     *     - candles: OHLCV candlestick data
     *
     * Symbol Format: tBTCUSD, tETHUSD (t + BASE + QUOTE, no separator)
     *
     * Rate Limits:
     *     - Max 30 subscriptions per connection
     *     - Heartbeat every 15 seconds
     *
     * Message Format:
     *     - Events: JSON objects with "event" field
     *     - Data: JSON arrays with [channelId, data]
     */

    /// <summary>
    /// Bitfinex WebSocket client for real-time market data streaming (API v2)
    /// </summary>
    public class BitfinexWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;
        private readonly Dictionary<int, ChannelInfo> _channelMap;

        public override string ExchangeName => "Bitfinex";
        protected override string WebSocketUrl => "wss://api-pub.bitfinex.com/ws/2";
        protected override string PrivateWebSocketUrl => "wss://api.bitfinex.com/ws/2";
        protected override int PingIntervalMs => 30000; // 30 seconds (server heartbeat every 15s)

        public BitfinexWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
            _channelMap = new Dictionary<int, ChannelInfo>();
        }

        /// <summary>
        /// Convert symbol format: BTC/USD -> tBTCUSD
        /// </summary>
        protected override string FormatSymbol(Market market)
        {
            return $"t{market.Base}{market.Quote}";
        }

        /// <summary>
        /// Convert Bitfinex symbol to standard format: tBTCUSD -> BTC/USD
        /// </summary>
        private string ToStandardSymbol(string bitfinexSymbol)
        {
            if (string.IsNullOrEmpty(bitfinexSymbol) || bitfinexSymbol.Length < 7)
                return bitfinexSymbol;

            // Remove 't' prefix and split (usually 3+3 or 3+4 chars)
            var symbol = bitfinexSymbol.StartsWith("t") ? bitfinexSymbol.Substring(1) : bitfinexSymbol;

            // Common patterns: BTCUSD (6), BTCUSDT (7), ETHUSD (6)
            if (symbol.Length == 6)
                return $"{symbol.Substring(0, 3)}/{symbol.Substring(3)}";
            else if (symbol.Length == 7)
                return $"{symbol.Substring(0, 3)}/{symbol.Substring(3)}";
            else if (symbol.Length == 8)
                return $"{symbol.Substring(0, 4)}/{symbol.Substring(4)}";

            return bitfinexSymbol;
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // Handle event messages (JSON objects)
                if (json.ValueKind == JsonValueKind.Object)
                {
                    await ProcessEventMessage(json);
                    return;
                }

                // Handle data messages (JSON arrays: [channelId, data])
                if (json.ValueKind == JsonValueKind.Array && json.GetArrayLength() >= 2)
                {
                    var channelId = json[0].GetInt32();

                    // Handle heartbeat
                    if (json[1].ValueKind == JsonValueKind.String && json[1].GetString() == "hb")
                    {
                        // Heartbeat received
                        return;
                    }

                    if (_channelMap.TryGetValue(channelId, out var channelInfo))
                    {
                        switch (channelInfo.Channel)
                        {
                            case "ticker":
                                await ProcessTicker(json, channelInfo);
                                break;
                            case "book":
                                await ProcessOrderbook(json, channelInfo);
                                break;
                            case "trades":
                                await ProcessTrades(json, channelInfo);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing message: {ex.Message}");
            }
        }

        private async Task ProcessEventMessage(JsonElement json)
        {
            var eventType = json.GetStringOrDefault("event");

            switch (eventType)
            {
                case "subscribed":
                    var channelId = json.GetInt32OrDefault("chanId");
                    var channel = json.GetStringOrDefault("channel");
                    var symbol = json.GetStringOrDefault("symbol");

                    _channelMap[channelId] = new ChannelInfo
                    {
                        ChannelId = channelId,
                        Channel = channel,
                        Symbol = symbol,
                        StandardSymbol = ToStandardSymbol(symbol)
                    };

                    RaiseInfo($"Subscribed to {channel} for {symbol} (chanId: {channelId})");
                    break;

                case "unsubscribed":
                    var unsubChanId = json.GetInt32OrDefault("chanId");
                    _channelMap.Remove(unsubChanId);
                    break;

                case "error":
                    var errorMsg = json.GetStringOrDefault("msg");
                    var errorCode = json.GetInt32OrDefault("code");
                    RaiseError($"Bitfinex error ({errorCode}): {errorMsg}");
                    break;

                case "info":
                    var version = json.GetInt32OrDefault("version");
                    if (version > 0)
                    {
                        RaiseInfo($"Bitfinex WebSocket API version: {version}");
                    }
                    break;
            }
        }

        private async Task ProcessTicker(JsonElement json, ChannelInfo channelInfo)
        {
            try
            {
                // Ticker format: [CHANNEL_ID, [BID, BID_SIZE, ASK, ASK_SIZE, DAILY_CHANGE, DAILY_CHANGE_RELATIVE, LAST_PRICE, VOLUME, HIGH, LOW]]
                if (json.GetArrayLength() < 2) return;

                var data = json[1];
                if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() < 10) return;

                var timestamp = TimeExtension.UnixTime;

                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = channelInfo.StandardSymbol,
                    timestamp = timestamp,
                    result = new STickerItem
                    {
                        timestamp = timestamp,
                        bidPrice = data[0].GetDecimal(),
                        bidQuantity = Math.Abs(data[1].GetDecimal()),
                        askPrice = data[2].GetDecimal(),
                        askQuantity = Math.Abs(data[3].GetDecimal()),
                        change = data[4].GetDecimal(),
                        percentage = data[5].GetDecimal() * 100, // Convert to percentage
                        closePrice = data[6].GetDecimal(),
                        volume = data[7].GetDecimal(),
                        highPrice = data[8].GetDecimal(),
                        lowPrice = data[9].GetDecimal()
                    }
                };

                InvokeTickerCallback(ticker);
                RecordMessageMetrics("ticker", channelInfo.StandardSymbol, 0, 0);
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing ticker: {ex.Message}");
            }
        }

        private async Task ProcessOrderbook(JsonElement json, ChannelInfo channelInfo)
        {
            try
            {
                if (json.GetArrayLength() < 2) return;

                var data = json[1];
                var timestamp = TimeExtension.UnixTime;
                var symbol = channelInfo.StandardSymbol;

                // Snapshot: [[PRICE, COUNT, AMOUNT], ...]
                // Update: [PRICE, COUNT, AMOUNT]
                bool isSnapshot = data.ValueKind == JsonValueKind.Array &&
                                  data.GetArrayLength() > 0 &&
                                  data[0].ValueKind == JsonValueKind.Array;

                if (isSnapshot || !_orderbookCache.ContainsKey(symbol))
                {
                    // Create new orderbook from snapshot
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

                    if (isSnapshot)
                    {
                        foreach (var entry in data.EnumerateArray())
                        {
                            if (entry.GetArrayLength() >= 3)
                            {
                                var price = entry[0].GetDecimal();
                                var count = entry[1].GetInt32();
                                var amount = entry[2].GetDecimal();

                                if (count > 0)
                                {
                                    var item = new SOrderBookItem
                                    {
                                        price = price,
                                        quantity = Math.Abs(amount)
                                    };

                                    // AMOUNT > 0 = bid, AMOUNT < 0 = ask
                                    if (amount > 0)
                                        orderbook.result.bids.Add(item);
                                    else
                                        orderbook.result.asks.Add(item);
                                }
                            }
                        }

                        // Sort: bids descending, asks ascending
                        orderbook.result.bids = orderbook.result.bids.OrderByDescending(b => b.price).ToList();
                        orderbook.result.asks = orderbook.result.asks.OrderBy(a => a.price).ToList();
                    }

                    _orderbookCache[symbol] = orderbook;
                    InvokeOrderbookCallback(orderbook);
                    RecordMessageMetrics("orderbook", symbol, 0, 0);
                }
                else if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() >= 3)
                {
                    // Update existing orderbook
                    var orderbook = _orderbookCache[symbol];
                    orderbook.timestamp = timestamp;
                    orderbook.result.timestamp = timestamp;

                    var price = data[0].GetDecimal();
                    var count = data[1].GetInt32();
                    var amount = data[2].GetDecimal();

                    var isBid = amount > 0;
                    var levels = isBid ? orderbook.result.bids : orderbook.result.asks;

                    if (count == 0)
                    {
                        // Remove level
                        levels.RemoveAll(l => l.price == price);
                    }
                    else
                    {
                        // Add or update level
                        var existing = levels.Find(l => l.price == price);
                        if (existing != null)
                        {
                            existing.quantity = Math.Abs(amount);
                        }
                        else
                        {
                            levels.Add(new SOrderBookItem
                            {
                                price = price,
                                quantity = Math.Abs(amount)
                            });

                            // Re-sort
                            if (isBid)
                                orderbook.result.bids = orderbook.result.bids.OrderByDescending(b => b.price).ToList();
                            else
                                orderbook.result.asks = orderbook.result.asks.OrderBy(a => a.price).ToList();
                        }
                    }

                    InvokeOrderbookCallback(orderbook);
                    RecordMessageMetrics("orderbook", symbol, 0, 0);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing orderbook: {ex.Message}");
            }
        }

        private async Task ProcessTrades(JsonElement json, ChannelInfo channelInfo)
        {
            try
            {
                if (json.GetArrayLength() < 2) return;

                var symbol = channelInfo.StandardSymbol;
                var trades = new List<STradeItem>();
                var timestamp = TimeExtension.UnixTime;

                var secondElement = json[1];

                // Check if it's an update message: [channelId, "te"/"tu", [data]]
                if (secondElement.ValueKind == JsonValueKind.String)
                {
                    var msgType = secondElement.GetString();
                    if ((msgType == "te" || msgType == "tu") && json.GetArrayLength() >= 3)
                    {
                        var tradeData = json[2];
                        if (tradeData.ValueKind == JsonValueKind.Array && tradeData.GetArrayLength() >= 4)
                        {
                            trades.Add(ParseTradeEntry(tradeData));
                        }
                    }
                }
                // Snapshot: [channelId, [[ID, MTS, AMOUNT, PRICE], ...]]
                else if (secondElement.ValueKind == JsonValueKind.Array)
                {
                    // Check if it's a nested array (snapshot) or single trade
                    if (secondElement.GetArrayLength() > 0 && secondElement[0].ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in secondElement.EnumerateArray())
                        {
                            if (entry.GetArrayLength() >= 4)
                            {
                                trades.Add(ParseTradeEntry(entry));
                            }
                        }
                    }
                    else if (secondElement.GetArrayLength() >= 4)
                    {
                        // Single trade array
                        trades.Add(ParseTradeEntry(secondElement));
                    }
                }

                if (trades.Count > 0)
                {
                    var trade = new STrade
                    {
                        exchange = ExchangeName,
                        symbol = symbol,
                        timestamp = timestamp,
                        result = trades
                    };

                    InvokeTradeCallback(trade);
                    RecordMessageMetrics("trades", symbol, 0, 0);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing trades: {ex.Message}");
            }
        }

        private STradeItem ParseTradeEntry(JsonElement entry)
        {
            // Format: [ID, MTS, AMOUNT, PRICE]
            var id = entry[0].GetInt64();
            var mts = entry[1].GetInt64();
            var amount = entry[2].GetDecimal();
            var price = entry[3].GetDecimal();

            return new STradeItem
            {
                tradeId = id.ToString(),
                timestamp = mts,
                sideType = amount > 0 ? SideType.Bid : SideType.Ask,
                orderType = OrderType.Market,
                price = price,
                quantity = Math.Abs(amount),
                amount = price * Math.Abs(amount)
            };
        }

        #region Subscription Methods

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var market = Market.Parse(symbol);
                var bitfinexSymbol = FormatSymbol(market);

                var subscribeMessage = new
                {
                    @event = "subscribe",
                    channel = "ticker",
                    symbol = bitfinexSymbol
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
                var market = Market.Parse(symbol);
                var bitfinexSymbol = FormatSymbol(market);

                var subscribeMessage = new
                {
                    @event = "subscribe",
                    channel = "book",
                    symbol = bitfinexSymbol,
                    prec = "P0",
                    freq = "F0",
                    len = "25"
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
                var market = Market.Parse(symbol);
                var bitfinexSymbol = FormatSymbol(market);

                var subscribeMessage = new
                {
                    @event = "subscribe",
                    channel = "trades",
                    symbol = bitfinexSymbol
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
            try
            {
                var market = Market.Parse(symbol);
                var bitfinexSymbol = FormatSymbol(market);
                var bitfinexInterval = ConvertInterval(interval);

                // Candles use key format: trade:{interval}:{symbol}
                var key = $"trade:{bitfinexInterval}:{bitfinexSymbol}";

                var subscribeMessage = new
                {
                    @event = "subscribe",
                    channel = "candles",
                    key = key
                };

                var json = JsonSerializer.Serialize(subscribeMessage);
                await SendMessageAsync(json);
                MarkSubscriptionActive("candles", symbol, interval);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Error subscribing to candles: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> UnsubscribeAsync(string channel, string symbol)
        {
            try
            {
                // Find channel ID by symbol and channel name
                var channelInfo = _channelMap.Values.FirstOrDefault(c =>
                    c.Channel == channel && c.StandardSymbol == symbol);

                if (channelInfo != null)
                {
                    var unsubscribeMessage = new
                    {
                        @event = "unsubscribe",
                        chanId = channelInfo.ChannelId
                    };

                    var json = JsonSerializer.Serialize(unsubscribeMessage);
                    await SendMessageAsync(json);
                }

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
            return false; // Bitfinex doesn't support batch subscription
        }

        #endregion

        protected override string CreatePingMessage()
        {
            // Bitfinex uses heartbeat messages automatically
            return JsonSerializer.Serialize(new { @event = "ping" });
        }

        private string ConvertInterval(string interval)
        {
            // Convert standard interval to Bitfinex format
            return interval.ToUpper() switch
            {
                "1M" => "1m",
                "5M" => "5m",
                "15M" => "15m",
                "30M" => "30m",
                "1H" => "1h",
                "3H" => "3h",
                "6H" => "6h",
                "12H" => "12h",
                "1D" => "1D",
                "1W" => "1W",
                "1MO" => "1M",
                _ => interval
            };
        }

        /// <summary>
        /// Internal class to track channel information
        /// </summary>
        private class ChannelInfo
        {
            public int ChannelId { get; set; }
            public string Channel { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string StandardSymbol { get; set; } = string.Empty;
        }
    }
}
