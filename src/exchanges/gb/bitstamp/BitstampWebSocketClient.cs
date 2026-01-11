using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Service;
using System.Text.Json;
using CCXT.Collector.Models.WebSocket;

namespace CCXT.Collector.Bitstamp
{
    /*
     * Bitstamp Exchange
     *
     * API Documentation:
     *     https://www.bitstamp.net/api/
     *
     * WebSocket API:
     *     wss://ws.bitstamp.net (public)
     *
     * Supported Channels:
     *     - live_trades_{symbol}: Trade stream
     *     - order_book_{symbol}: Full orderbook snapshots
     *     - diff_order_book_{symbol}: Incremental orderbook updates
     *     - live_orders_{symbol}: Live order events
     *
     * Symbol Format: btcusd (lowercase, no separator)
     * Supported Markets: USD, EUR, GBP
     *
     * Rate Limits:
     *     - 400 requests per second
     *     - 10,000 requests per 10 minutes
     *
     * Note: WebSocket API does not provide initial orderbook snapshots
     *       for diff_order_book channel. Use REST API for initial snapshot.
     */

    /// <summary>
    /// Bitstamp WebSocket client for real-time market data streaming
    /// </summary>
    public class BitstampWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;

        public override string ExchangeName => "Bitstamp";
        protected override string WebSocketUrl => "wss://ws.bitstamp.net";
        protected override int PingIntervalMs => 30000; // 30 seconds

        public BitstampWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
        }

        /// <summary>
        /// Convert symbol format: BTC/USD -> btcusd (lowercase, no separator)
        /// </summary>
        protected override string FormatSymbol(Market market)
        {
            return $"{market.Base}{market.Quote}".ToLower();
        }

        /// <summary>
        /// Convert Bitstamp symbol to standard format: btcusd -> BTC/USD
        /// </summary>
        private string ToStandardSymbol(string bitstampSymbol)
        {
            if (string.IsNullOrEmpty(bitstampSymbol) || bitstampSymbol.Length < 5)
                return bitstampSymbol?.ToUpper() ?? "";

            // Common quote currencies (3 or 4 chars)
            string[] quoteCurrencies = { "usdt", "usdc", "usd", "eur", "gbp", "btc", "eth" };

            bitstampSymbol = bitstampSymbol.ToLower();

            foreach (var quote in quoteCurrencies)
            {
                if (bitstampSymbol.EndsWith(quote))
                {
                    var baseLen = bitstampSymbol.Length - quote.Length;
                    if (baseLen > 0)
                    {
                        var baseCurrency = bitstampSymbol.Substring(0, baseLen).ToUpper();
                        var quoteCurrency = quote.ToUpper();
                        return $"{baseCurrency}/{quoteCurrency}";
                    }
                }
            }

            return bitstampSymbol.ToUpper();
        }

        /// <summary>
        /// Extract symbol from channel name: live_trades_btcusd -> btcusd
        /// </summary>
        private string ExtractSymbolFromChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel))
                return "";

            // Channel format: {channel_type}_{symbol}
            // Examples: live_trades_btcusd, order_book_btcusd, diff_order_book_btcusd
            var parts = channel.Split('_');
            if (parts.Length >= 2)
            {
                // Last part is the symbol
                return parts[parts.Length - 1];
            }

            return channel;
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                var eventType = json.GetStringOrDefault("event");
                var channel = json.GetStringOrDefault("channel");

                // Handle subscription confirmation
                if (eventType == "bts:subscription_succeeded")
                {
                    RaiseInfo($"Subscription confirmed: {channel}");
                    return;
                }

                // Handle unsubscription confirmation
                if (eventType == "bts:unsubscription_succeeded")
                {
                    RaiseInfo($"Unsubscription confirmed: {channel}");
                    return;
                }

                // Handle request reconnect
                if (eventType == "bts:request_reconnect")
                {
                    RaiseInfo("Server requested reconnect");
                    await HandleReconnectAsync();
                    return;
                }

                // Handle error
                if (eventType == "bts:error")
                {
                    var errorMsg = "Unknown error";
                    if (json.TryGetProperty("data", out var errorData))
                    {
                        errorMsg = errorData.GetStringOrDefault("message");
                        if (string.IsNullOrEmpty(errorMsg))
                            errorMsg = "Unknown error";
                    }
                    RaiseError($"Bitstamp error: {errorMsg}");
                    return;
                }

                // Extract symbol from channel name
                var bitstampSymbol = ExtractSymbolFromChannel(channel);
                var standardSymbol = ToStandardSymbol(bitstampSymbol);

                // Handle trade event
                if (eventType == "trade")
                {
                    ProcessTrade(json, standardSymbol);
                    return;
                }

                // Handle orderbook data event
                if (eventType == "data")
                {
                    if (channel.Contains("order_book") || channel.Contains("diff_order_book"))
                    {
                        ProcessOrderbook(json, standardSymbol, channel.Contains("diff_order_book"));
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing message: {ex.Message}");
            }
        }

        private void ProcessTrade(JsonElement json, string symbol)
        {
            try
            {
                if (!json.TryGetProperty("data", out var data))
                    return;

                var timestamp = data.GetInt64OrDefault("timestamp") * 1000; // Convert to milliseconds
                if (timestamp == 0) timestamp = TimeExtension.UnixTime;

                var tradeType = data.GetInt32OrDefault("type"); // 0 = buy, 1 = sell
                var price = data.GetDecimalOrDefault("price");
                var amount = data.GetDecimalOrDefault("amount");

                var trade = new STrade
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = timestamp,
                    result = new List<STradeItem>
                    {
                        new STradeItem
                        {
                            tradeId = data.GetInt64OrDefault("id").ToString(),
                            timestamp = timestamp,
                            sideType = tradeType == 0 ? SideType.Bid : SideType.Ask,
                            orderType = OrderType.Market,
                            price = price,
                            quantity = amount,
                            amount = price * amount
                        }
                    }
                };

                InvokeTradeCallback(trade);
                RecordMessageMetrics("trades", symbol, 0, 0);

                // Also generate ticker update from trade data
                // Bitstamp doesn't have a separate ticker channel, so we use trades
                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = timestamp,
                    result = new STickerItem
                    {
                        timestamp = timestamp,
                        closePrice = price,
                        volume = amount,
                        // bid/ask will be empty as this comes from trade data
                        bidPrice = 0,
                        askPrice = 0
                    }
                };

                InvokeTickerCallback(ticker);
                RecordMessageMetrics("ticker", symbol, 0, 0);
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing trade: {ex.Message}");
            }
        }

        private void ProcessOrderbook(JsonElement json, string symbol, bool isDiff)
        {
            try
            {
                if (!json.TryGetProperty("data", out var data))
                    return;

                var timestamp = data.GetInt64OrDefault("timestamp") * 1000; // Convert to milliseconds
                if (timestamp == 0) timestamp = data.GetInt64OrDefault("microtimestamp") / 1000;
                if (timestamp == 0) timestamp = TimeExtension.UnixTime;

                // Initialize or get cached orderbook
                if (!_orderbookCache.ContainsKey(symbol))
                {
                    _orderbookCache[symbol] = new SOrderBook
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
                }

                var orderbook = _orderbookCache[symbol];
                orderbook.timestamp = timestamp;
                orderbook.result.timestamp = timestamp;

                if (isDiff)
                {
                    // Process diff orderbook (incremental updates)
                    ProcessOrderbookDiff(data, orderbook);
                }
                else
                {
                    // Process full orderbook snapshot
                    ProcessOrderbookSnapshot(data, orderbook);
                }

                InvokeOrderbookCallback(orderbook);
                RecordMessageMetrics("orderbook", symbol, 0, 0);
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing orderbook: {ex.Message}");
            }
        }

        private void ProcessOrderbookSnapshot(JsonElement data, SOrderBook orderbook)
        {
            // Clear existing data for snapshot
            orderbook.result.bids.Clear();
            orderbook.result.asks.Clear();

            // Process bids [[price, amount], ...]
            if (data.TryGetProperty("bids", out var bids) && bids.ValueKind == JsonValueKind.Array)
            {
                foreach (var bid in bids.EnumerateArray())
                {
                    if (bid.ValueKind == JsonValueKind.Array)
                    {
                        var arr = bid.EnumerateArray().ToArray();
                        if (arr.Length >= 2)
                        {
                            var price = ParseDecimal(arr[0]);
                            var qty = ParseDecimal(arr[1]);
                            if (price > 0 && qty > 0)
                            {
                                orderbook.result.bids.Add(new SOrderBookItem
                                {
                                    price = price,
                                    quantity = qty
                                });
                            }
                        }
                    }
                }
            }

            // Process asks [[price, amount], ...]
            if (data.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.Array)
            {
                foreach (var ask in asks.EnumerateArray())
                {
                    if (ask.ValueKind == JsonValueKind.Array)
                    {
                        var arr = ask.EnumerateArray().ToArray();
                        if (arr.Length >= 2)
                        {
                            var price = ParseDecimal(arr[0]);
                            var qty = ParseDecimal(arr[1]);
                            if (price > 0 && qty > 0)
                            {
                                orderbook.result.asks.Add(new SOrderBookItem
                                {
                                    price = price,
                                    quantity = qty
                                });
                            }
                        }
                    }
                }
            }
        }

        private void ProcessOrderbookDiff(JsonElement data, SOrderBook orderbook)
        {
            // Process bid updates
            if (data.TryGetProperty("bids", out var bids) && bids.ValueKind == JsonValueKind.Array)
            {
                foreach (var bid in bids.EnumerateArray())
                {
                    if (bid.ValueKind == JsonValueKind.Array)
                    {
                        var arr = bid.EnumerateArray().ToArray();
                        if (arr.Length >= 2)
                        {
                            var price = ParseDecimal(arr[0]);
                            var qty = ParseDecimal(arr[1]);
                            ApplyOrderbookUpdate(orderbook.result.bids, price, qty, true);
                        }
                    }
                }
            }

            // Process ask updates
            if (data.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.Array)
            {
                foreach (var ask in asks.EnumerateArray())
                {
                    if (ask.ValueKind == JsonValueKind.Array)
                    {
                        var arr = ask.EnumerateArray().ToArray();
                        if (arr.Length >= 2)
                        {
                            var price = ParseDecimal(arr[0]);
                            var qty = ParseDecimal(arr[1]);
                            ApplyOrderbookUpdate(orderbook.result.asks, price, qty, false);
                        }
                    }
                }
            }
        }

        private decimal ParseDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                if (decimal.TryParse(element.GetString(), out var result))
                    return result;
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDecimal();
            }
            return 0;
        }

        private void ApplyOrderbookUpdate(List<SOrderBookItem> levels, decimal price, decimal qty, bool isBid)
        {
            if (qty == 0)
            {
                // Remove level
                levels.RemoveAll(l => l.price == price);
                return;
            }

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

        #region Subscription Methods

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var market = Market.Parse(symbol);
                var bitstampSymbol = FormatSymbol(market);

                // Bitstamp doesn't have a dedicated ticker channel
                // Use live_trades as ticker since it provides real-time price updates
                var channel = $"live_trades_{bitstampSymbol}";

                var subscribeMessage = new
                {
                    @event = "bts:subscribe",
                    data = new { channel }
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
                var bitstampSymbol = FormatSymbol(market);

                // Use order_book for full snapshots (more accurate)
                var channel = $"order_book_{bitstampSymbol}";

                var subscribeMessage = new
                {
                    @event = "bts:subscribe",
                    data = new { channel }
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
                var bitstampSymbol = FormatSymbol(market);

                var channel = $"live_trades_{bitstampSymbol}";

                var subscribeMessage = new
                {
                    @event = "bts:subscribe",
                    data = new { channel }
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
            // Bitstamp WebSocket API does not support candles/OHLCV data
            // Candle data must be fetched via REST API
            RaiseError("Bitstamp WebSocket does not support candles. Use REST API instead.");
            return false;
        }

        public override async Task<bool> UnsubscribeAsync(string channel, string symbol)
        {
            try
            {
                var market = Market.Parse(symbol);
                var bitstampSymbol = FormatSymbol(market);

                var channelName = channel.ToLower() switch
                {
                    "ticker" => $"live_trades_{bitstampSymbol}",
                    "orderbook" => $"order_book_{bitstampSymbol}",
                    "trades" => $"live_trades_{bitstampSymbol}",
                    _ => $"{channel}_{bitstampSymbol}"
                };

                var unsubscribeMessage = new
                {
                    @event = "bts:unsubscribe",
                    data = new { channel = channelName }
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
            return false; // Bitstamp requires individual subscriptions
        }

        #endregion

        protected override string CreatePingMessage()
        {
            // Bitstamp uses heartbeat channel for keep-alive
            // Return empty as Bitstamp doesn't require explicit ping messages
            return string.Empty;
        }

        protected override async Task SendPingAsync()
        {
            // Bitstamp WebSocket doesn't require explicit ping messages
            // The server will send heartbeat events periodically
            // We override to prevent sending empty messages
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
    }
}
