using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Service;
using System.Text.Json;
using CCXT.Collector.Models.WebSocket;

namespace CCXT.Collector.Mexc
{
    /*
     * MEXC Exchange
     *
     * API Documentation:
     *     https://mexcdevelop.github.io/apidocs/spot_v3_en/
     *
     * WebSocket API:
     *     wss://wbs.mexc.com/ws (public)
     *
     * Supported Channels:
     *     - spot@public.aggre.bookTicker.v3.api.pb: Best bid/ask ticker
     *     - spot@public.aggre.depth.v3.api.pb: Incremental orderbook depth
     *     - spot@public.aggre.deals.v3.api.pb: Trade stream
     *     - spot@public.limit.depth.v3.api.pb: Snapshot orderbook
     *     - spot@public.kline.v3.api.pb: Candlestick/Kline data
     *
     * Symbol Format: BTCUSDT (uppercase, no separator)
     *
     * Rate Limits:
     *     - Max 30 subscriptions per connection
     *     - 100 requests/second
     *     - Connection valid for max 24 hours
     */

    /// <summary>
    /// MEXC WebSocket client for real-time market data streaming
    /// </summary>
    public class MexcWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;

        public override string ExchangeName => "MEXC";
        protected override string WebSocketUrl => "wss://wbs.mexc.com/ws";
        protected override int PingIntervalMs => 20000; // 20 seconds (server disconnects after 60s without ping)

        public MexcWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
        }

        /// <summary>
        /// Convert symbol format: BTC/USDT -> BTCUSDT (uppercase, no separator)
        /// </summary>
        protected override string FormatSymbol(Market market)
        {
            return $"{market.Base}{market.Quote}".ToUpper();
        }

        /// <summary>
        /// Convert MEXC symbol to standard format: BTCUSDT -> BTC/USDT
        /// </summary>
        private string ToStandardSymbol(string mexcSymbol)
        {
            if (string.IsNullOrEmpty(mexcSymbol) || mexcSymbol.Length < 5)
                return mexcSymbol;

            // Common quote currencies
            string[] quoteCurrencies = { "USDT", "USDC", "BTC", "ETH", "MX" };

            foreach (var quote in quoteCurrencies)
            {
                if (mexcSymbol.EndsWith(quote))
                {
                    var baseLen = mexcSymbol.Length - quote.Length;
                    if (baseLen > 0)
                    {
                        return $"{mexcSymbol.Substring(0, baseLen)}/{quote}";
                    }
                }
            }

            return mexcSymbol;
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // Handle PONG response
                if (json.TryGetProperty("msg", out var msgProp) && msgProp.GetString() == "PONG")
                {
                    return;
                }

                // Handle subscription confirmation
                if (json.TryGetProperty("code", out var codeProp) && codeProp.GetInt32() == 0)
                {
                    if (json.TryGetProperty("msg", out var subMsg))
                    {
                        RaiseInfo($"Subscription confirmed: {subMsg.GetString()}");
                    }
                    return;
                }

                // Handle channel data
                if (json.TryGetProperty("channel", out var channelProp))
                {
                    var channel = channelProp.GetString() ?? "";
                    var symbol = json.GetStringOrDefault("symbol");
                    var standardSymbol = ToStandardSymbol(symbol);

                    if (channel.Contains("bookTicker"))
                    {
                        ProcessTicker(json, standardSymbol);
                    }
                    else if (channel.Contains("depth"))
                    {
                        ProcessOrderbook(json, standardSymbol);
                    }
                    else if (channel.Contains("deals"))
                    {
                        ProcessTrades(json, standardSymbol);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing message: {ex.Message}");
            }
        }

        private void ProcessTicker(JsonElement json, string symbol)
        {
            try
            {
                // Response: publicbookticker with bidprice, bidquantity, askprice, askquantity
                if (!json.TryGetProperty("publicbookticker", out var data))
                    return;

                var timestamp = json.GetInt64OrDefault("sendtime");
                if (timestamp == 0) timestamp = TimeExtension.UnixTime;

                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = timestamp,
                    result = new STickerItem
                    {
                        timestamp = timestamp,
                        bidPrice = data.GetDecimalOrDefault("bidprice"),
                        bidQuantity = data.GetDecimalOrDefault("bidquantity"),
                        askPrice = data.GetDecimalOrDefault("askprice"),
                        askQuantity = data.GetDecimalOrDefault("askquantity")
                    }
                };

                InvokeTickerCallback(ticker);
                RecordMessageMetrics("ticker", symbol, 0, 0);
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing ticker: {ex.Message}");
            }
        }

        private void ProcessOrderbook(JsonElement json, string symbol)
        {
            try
            {
                // Response: publicincreasedepths with bidsList[], asksList[]
                if (!json.TryGetProperty("publicincreasedepths", out var data))
                    return;

                var timestamp = json.GetInt64OrDefault("sendtime");
                if (timestamp == 0) timestamp = TimeExtension.UnixTime;

                // Check if we have a cached orderbook
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

                // Process bids
                if (data.TryGetProperty("bidsList", out var bids) && bids.ValueKind == JsonValueKind.Array)
                {
                    foreach (var bid in bids.EnumerateArray())
                    {
                        var price = bid.GetDecimalOrDefault("price");
                        var qty = bid.GetDecimalOrDefault("quantity");
                        ApplyOrderbookUpdate(orderbook.result.bids, price, qty, true);
                    }
                }

                // Process asks
                if (data.TryGetProperty("asksList", out var asks) && asks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ask in asks.EnumerateArray())
                    {
                        var price = ask.GetDecimalOrDefault("price");
                        var qty = ask.GetDecimalOrDefault("quantity");
                        ApplyOrderbookUpdate(orderbook.result.asks, price, qty, false);
                    }
                }

                InvokeOrderbookCallback(orderbook);
                RecordMessageMetrics("orderbook", symbol, 0, 0);
            }
            catch (Exception ex)
            {
                RaiseError($"Error processing orderbook: {ex.Message}");
            }
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

        private void ProcessTrades(JsonElement json, string symbol)
        {
            try
            {
                // Response: publicdeals.dealsList[] with price, quantity, tradetype (1=buy, 2=sell), time
                if (!json.TryGetProperty("publicdeals", out var data))
                    return;

                if (!data.TryGetProperty("dealsList", out var dealsList) || dealsList.ValueKind != JsonValueKind.Array)
                    return;

                var trades = new List<STradeItem>();
                var timestamp = json.GetInt64OrDefault("sendtime");
                if (timestamp == 0) timestamp = TimeExtension.UnixTime;

                foreach (var deal in dealsList.EnumerateArray())
                {
                    var price = deal.GetDecimalOrDefault("price");
                    var qty = deal.GetDecimalOrDefault("quantity");
                    var tradeType = deal.GetInt32OrDefault("tradetype"); // 1=buy, 2=sell
                    var tradeTime = deal.GetInt64OrDefault("time");

                    trades.Add(new STradeItem
                    {
                        tradeId = tradeTime.ToString(),
                        timestamp = tradeTime,
                        sideType = tradeType == 1 ? SideType.Bid : SideType.Ask,
                        orderType = OrderType.Market,
                        price = price,
                        quantity = qty,
                        amount = price * qty
                    });
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

        #region Subscription Methods

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var market = Market.Parse(symbol);
                var mexcSymbol = FormatSymbol(market);

                // Use bookTicker for ticker data (best bid/ask)
                var channel = $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{mexcSymbol}";

                var subscribeMessage = new
                {
                    method = "SUBSCRIPTION",
                    @params = new[] { channel }
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
                var mexcSymbol = FormatSymbol(market);

                // Use incremental depth stream
                var channel = $"spot@public.aggre.depth.v3.api.pb@100ms@{mexcSymbol}";

                var subscribeMessage = new
                {
                    method = "SUBSCRIPTION",
                    @params = new[] { channel }
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
                var mexcSymbol = FormatSymbol(market);

                var channel = $"spot@public.aggre.deals.v3.api.pb@100ms@{mexcSymbol}";

                var subscribeMessage = new
                {
                    method = "SUBSCRIPTION",
                    @params = new[] { channel }
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
                var mexcSymbol = FormatSymbol(market);
                var mexcInterval = ConvertInterval(interval);

                var channel = $"spot@public.kline.v3.api.pb@{mexcSymbol}@{mexcInterval}";

                var subscribeMessage = new
                {
                    method = "SUBSCRIPTION",
                    @params = new[] { channel }
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
                var market = Market.Parse(symbol);
                var mexcSymbol = FormatSymbol(market);

                var channelName = channel.ToLower() switch
                {
                    "ticker" => $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{mexcSymbol}",
                    "orderbook" => $"spot@public.aggre.depth.v3.api.pb@100ms@{mexcSymbol}",
                    "trades" => $"spot@public.aggre.deals.v3.api.pb@100ms@{mexcSymbol}",
                    _ => $"spot@public.{channel}.v3.api.pb@{mexcSymbol}"
                };

                var unsubscribeMessage = new
                {
                    method = "UNSUBSCRIPTION",
                    @params = new[] { channelName }
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
            return true; // MEXC supports multiple channels in one subscription
        }

        protected override async Task<bool> SendBatchSubscriptionsAsync(List<KeyValuePair<string, SubscriptionInfo>> subscriptions)
        {
            try
            {
                var channels = new List<string>();

                foreach (var kvp in subscriptions)
                {
                    var subscription = kvp.Value;
                    var market = Market.Parse(subscription.Symbol);
                    var mexcSymbol = FormatSymbol(market);

                    var channelName = subscription.Channel.ToLower() switch
                    {
                        "ticker" => $"spot@public.aggre.bookTicker.v3.api.pb@100ms@{mexcSymbol}",
                        "orderbook" => $"spot@public.aggre.depth.v3.api.pb@100ms@{mexcSymbol}",
                        "trades" => $"spot@public.aggre.deals.v3.api.pb@100ms@{mexcSymbol}",
                        _ => null
                    };

                    if (channelName != null)
                        channels.Add(channelName);
                }

                if (channels.Count > 0)
                {
                    var subscribeMessage = new
                    {
                        method = "SUBSCRIPTION",
                        @params = channels.ToArray()
                    };

                    await SendMessageAsync(JsonSerializer.Serialize(subscribeMessage));
                    RaiseInfo($"Subscribed to {channels.Count} channels");
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
            return JsonSerializer.Serialize(new { method = "PING" });
        }

        private string ConvertInterval(string interval)
        {
            // Convert standard interval to MEXC format
            return interval.ToUpper() switch
            {
                "1M" => "Min1",
                "5M" => "Min5",
                "15M" => "Min15",
                "30M" => "Min30",
                "1H" => "Min60",
                "4H" => "Hour4",
                "8H" => "Hour8",
                "1D" => "Day1",
                "1W" => "Week1",
                "1MO" => "Month1",
                _ => interval
            };
        }
    }
}
