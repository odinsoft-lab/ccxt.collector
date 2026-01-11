using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Models.WebSocket;
using CCXT.Collector.Core.Infrastructure; 
using CCXT.Collector.Service;
using System.Text.Json;

namespace CCXT.Collector.Bitget
{
    /*
     * Bitget Support Markets: USDT, USDC, BTC, ETH, BGB
     *
     * API Documentation (V2):
     *     https://www.bitget.com/api-doc/spot/websocket/intro
     *     https://www.bitget.com/api-doc/common/websocket-intro
     *
     * WebSocket API V2:
     * overview
     *     https://www.bitget.com/api-doc/common/websocket-intro
     * public
     *     https://www.bitget.com/api-doc/spot/websocket/public/Tickers-Channel
     *     https://www.bitget.com/api-doc/spot/websocket/public/Candlesticks-Channel
     *     https://www.bitget.com/api-doc/spot/websocket/public/Trades-Channel
     *     https://www.bitget.com/api-doc/spot/websocket/public/Depth-Channel
     * private
     *     https://www.bitget.com/api-doc/spot/websocket/private/Account-Channel
     *     https://www.bitget.com/api-doc/spot/websocket/private/Order-Channel
     *     https://www.bitget.com/api-doc/spot/websocket/private/Place-Order-Channel
     * error
     *     https://www.bitget.com/api-doc/spot/error-code/websocket
     *
     * V2 API Notes:
     *     - V1 API (wss://ws.bitget.com/spot/v1/stream) is decommissioned
     *     - V2 uses separate public/private WebSocket endpoints
     *     - instType must be "SPOT" (uppercase) for spot trading
     *     - Ping is plain string "ping", pong is "pong"
     *     - Connection timeout: 2 minutes without ping
     *     - Max 240 subscription requests/hour, 1000 channels/connection
     *
     * Fees:
     *     https://www.bitget.com/fee
     */
    /// <summary>
    /// Bitget WebSocket client for real-time data streaming (V2 API)
    /// </summary>
    public class BitgetWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;
        private readonly Dictionary<string, long> _lastUpdateIds;

        public override string ExchangeName => "Bitget";

        // V2 API WebSocket URLs
        protected override string WebSocketUrl => "wss://ws.bitget.com/v2/ws/public";
        protected override string PrivateWebSocketUrl => "wss://ws.bitget.com/v2/ws/private";

        // V2 requires ping every 30 seconds (connection closes after 2 minutes without ping)
        protected override int PingIntervalMs => 30000;

        public BitgetWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
            _lastUpdateIds = new Dictionary<string, long>();
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                // V2 API: Handle plain string ping/pong first
                if (message == "pong")
                {
                    // Pong response received, connection is alive
                    return;
                }

                if (message == "ping")
                {
                    // Server sent ping, respond with pong
                    await SendMessageAsync("pong");
                    return;
                }

                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // V2 API: Check for "event" field in responses
                var eventType = json.GetStringOrDefault("event");

                // Handle subscription responses
                if (eventType == "subscribe" || eventType == "subscription")
                {
                    var code = json.GetStringOrDefault("code");
                    if (code != "0" && code != null && code != "")
                    {
                        var errorMsg = json.GetStringOrDefault("msg", "Unknown error");
                        var arg = json.TryGetProperty("arg", out var argProp) ? JsonSerializer.Serialize(argProp) : "";
                        RaiseError($"Subscription failed - Code: {code}, Msg: {errorMsg}, Arg: {arg}");
                    }
                    return;
                }

                // Handle error responses
                if (eventType == "error")
                {
                    var errorMsg = json.GetStringOrDefault("msg", "Unknown error");
                    var errorCode = json.GetStringOrDefault("code", "");
                    var arg = json.TryGetProperty("arg", out var argProp) ? JsonSerializer.Serialize(argProp) : "";
                    RaiseError($"Bitget Error {errorCode}: {errorMsg} (arg: {arg})");
                    return;
                }

                // Handle data messages
                var action = json.GetStringOrDefault("action");
                if (action == "update" || action == "snapshot" || json.TryGetProperty("data", out _))
                {
                    if (json.TryGetProperty("arg", out var arg))
                    {
                        var channel = arg.GetStringOrDefault("channel");
                        var instId = arg.GetStringOrDefault("instId");

                        if (isPrivate)
                        {
                            // Handle private data
                            switch (channel)
                            {
                                case "account":
                                    await ProcessAccountData(json);
                                    break;
                                case "orders":
                                    await ProcessOrderData(json);
                                    break;
                                case "positions":
                                    await ProcessPositionData(json);
                                    break;
                            }
                        }
                        else
                        {
                            // Handle public data
                            switch (channel)
                            {
                                case "books":
                                case "books5":
                                case "books15":
                                case "depth":
                                case "orderbook":
                                    await ProcessOrderbookData(json);
                                    break;
                                case "trade":
                                case "trades":
                                    await ProcessTradeData(json);
                                    break;
                                case "ticker":
                                case "tickers":
                                    await ProcessTickerData(json);
                                    break;
                                case "candle1m":
                                case "candle5m":
                                case "candle15m":
                                case "candle30m":
                                case "candle1H":
                                case "candle4H":
                                case "candle1D":
                                    await ProcessCandleData(json);
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Message processing error: {ex.Message}");
            }
        }


        private async Task ProcessOrderbookData(JsonElement json)
        {
            try
            {
                if (!json.TryGetProperty("arg", out var arg))
                    return;

                var instId = arg.GetStringOrDefault("instId");
                if (string.IsNullOrEmpty(instId))
                    return;

                if (!(json.TryGetArray("data", out var dataProp)))
                    return;

                var data = dataProp.EnumerateArray().FirstOrDefault();
                if (data.ValueKind == JsonValueKind.Undefined)
                    return;

                var symbol = ParsingHelpers.NormalizeSymbol(instId);
                var timestamp = data.GetInt64OrDefault("ts", TimeExtension.UnixTime);
                var action = json.GetStringOrDefault("action");

                var orderbook = new SOrderBook
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = timestamp,
                    result = new SOrderBookData
                    {
                        timestamp = timestamp,
                        asks = new List<SOrderBookItem>(),
                        bids = new List<SOrderBookItem>()
                    }
                };

                // Process asks
                if (data.TryGetArray("asks", out var asks))
                {
                    foreach (var ask in asks.EnumerateArray())
                    {
                        if (ask.GetArrayLength() < 2)
                            continue;

                        var price = ask[0].GetDecimalValue();
                        var amount = ask[1].GetDecimalValue();

                        orderbook.result.asks.Add(new SOrderBookItem
                        {
                            price = price,
                            quantity = amount,
                            amount = price * amount
                        });
                    }
                }

                // Process bids
                if (data.TryGetArray("bids", out var bids))
                {
                    foreach (var bid in bids.EnumerateArray())
                    {
                        if (bid.GetArrayLength() < 2)
                            continue;

                        var price = bid[0].GetDecimalValue();
                        var amount = bid[1].GetDecimalValue();

                        orderbook.result.bids.Add(new SOrderBookItem
                        {
                            price = price,
                            quantity = amount,
                            amount = price * amount
                        });
                    }
                }

                // Sort orderbook
                orderbook.result.asks = orderbook.result.asks.OrderBy(a => a.price).ToList();
                orderbook.result.bids = orderbook.result.bids.OrderByDescending(b => b.price).ToList();

                // Handle incremental updates
                if (action == "update" && _orderbookCache.ContainsKey(symbol))
                {
                    var cached = _orderbookCache[symbol];
                    MergeOrderbook(cached, orderbook);
                    InvokeOrderbookCallback(cached);
                }
                else
                {
                    _orderbookCache[symbol] = orderbook;
                    InvokeOrderbookCallback(orderbook);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Orderbook processing error: {ex.Message}");
            }
        }

        private void MergeOrderbook(SOrderBook cached, SOrderBook update)
        {
            // Merge asks
            foreach (var ask in update.result.asks)
            {
                var existing = cached.result.asks.FirstOrDefault(a => a.price == ask.price);
                if (existing != null)
                {
                    if (ask.quantity == 0)
                        cached.result.asks.Remove(existing);
                    else
                        existing.quantity = ask.quantity;
                }
                else if (ask.quantity > 0)
                {
                    cached.result.asks.Add(ask);
                }
            }

            // Merge bids
            foreach (var bid in update.result.bids)
            {
                var existing = cached.result.bids.FirstOrDefault(b => b.price == bid.price);
                if (existing != null)
                {
                    if (bid.quantity == 0)
                        cached.result.bids.Remove(existing);
                    else
                        existing.quantity = bid.quantity;
                }
                else if (bid.quantity > 0)
                {
                    cached.result.bids.Add(bid);
                }
            }

            cached.result.asks = cached.result.asks.OrderBy(a => a.price).ToList();
            cached.result.bids = cached.result.bids.OrderByDescending(b => b.price).ToList();
            cached.timestamp = update.timestamp;
        }

        private async Task ProcessTradeData(JsonElement json)
        {
            try
            {
                if (!json.TryGetProperty("arg", out var arg))
                    return;

                var instId = arg.GetStringOrDefault("instId");
                if (string.IsNullOrEmpty(instId))
                    return;

                if (!(json.TryGetArray("data", out var dataProp)))
                    return;

                var symbol = ParsingHelpers.NormalizeSymbol(instId);
                var trades = new List<STradeItem>();
                long latestTimestamp = 0;

                // Bitget sends trade data as array of trade objects
                foreach (var trade in dataProp.EnumerateArray())
                {
                    var timestamp = trade.GetInt64OrDefault("ts", TimeExtension.UnixTime);
                    if (timestamp > latestTimestamp)
                        latestTimestamp = timestamp;

                    trades.Add(new STradeItem
                    {
                        tradeId = trade.GetStringOrDefault("tradeId"),
                        timestamp = timestamp,
                        price = trade.GetDecimalOrDefault("price"),
                        quantity = trade.GetDecimalOrDefault("size"),
                        amount = (trade.GetDecimalOrDefault("price")) * (trade.GetDecimalOrDefault("size")),
                        sideType = trade.GetStringOrDefault("side") == "buy" ? SideType.Bid : SideType.Ask,
                        orderType = OrderType.Limit
                    });
                }

                if (trades.Count > 0)
                {
                    var completeOrder = new STrade
                    {
                        exchange = ExchangeName,
                        symbol = symbol,
                        timestamp = latestTimestamp,
                        result = trades
                    };

                    InvokeTradeCallback(completeOrder);
                }
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
                if (!json.TryGetProperty("arg", out var arg))
                    return;

                var instId = arg.GetStringOrDefault("instId");
                if (string.IsNullOrEmpty(instId))
                    return;

                if (!(json.TryGetArray("data", out var dataProp)))
                    return;

                var data = dataProp.EnumerateArray().FirstOrDefault();
                if (data.ValueKind == JsonValueKind.Undefined)
                    return;

                var symbol = ParsingHelpers.NormalizeSymbol(instId);
                var timestamp = data.GetInt64OrDefault("ts", TimeExtension.UnixTime);

                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = symbol,
                    timestamp = timestamp,
                    result = new STickerItem
                    {
                        timestamp = timestamp,
                        closePrice = data.GetDecimalOrDefault("lastPr"),
                        askPrice = data.GetDecimalOrDefault("askPr"),
                        bidPrice = data.GetDecimalOrDefault("bidPr"),
                        openPrice = data.GetDecimalOrDefault("open24h"),
                        highPrice = data.GetDecimalOrDefault("high24h"),
                        lowPrice = data.GetDecimalOrDefault("low24h"),
                        volume = data.GetDecimalOrDefault("baseVolume"),
                        quoteVolume = data.GetDecimalOrDefault("quoteVolume"),
                        change = data.GetDecimalOrDefault("change24h"),
                        percentage = data.GetDecimalOrDefault("changeUtc24h")
                    }
                };

                InvokeTickerCallback(ticker);
            }
            catch (Exception ex)
            {
                RaiseError($"Ticker processing error: {ex.Message}");
            }
        }

        private async Task ProcessCandleData(JsonElement json)
        {
            try
            {
                if (!json.TryGetProperty("arg", out var arg))
                    return;

                var channel = arg.GetStringOrDefault("channel");

                var instId = arg.GetStringOrDefault("instId");
                if (string.IsNullOrEmpty(instId))
                    return;

                if (!(json.TryGetArray("data", out var data)))
                    return;

                var symbol = ParsingHelpers.NormalizeSymbol(instId);
                var interval = ParsingHelpers.FromBitgetChannelInterval(channel);
                var intervalMs = ParsingHelpers.IntervalToMilliseconds(interval);

                var candleItems = new List<SCandleItem>();
                long latestTimestamp = 0;

                foreach (var candle in data.EnumerateArray())
                {
                    var timestamp = candle.GetInt64OrDefault("ts");
                    if (timestamp > latestTimestamp)
                        latestTimestamp = timestamp;

                    candleItems.Add(new SCandleItem
                    {
                        openTime = timestamp,
                        closeTime = timestamp + intervalMs,
                        open = candle.GetDecimalOrDefault("o"),
                        high = candle.GetDecimalOrDefault("h"),
                        low = candle.GetDecimalOrDefault("l"),
                        close = candle.GetDecimalOrDefault("c"),
                        volume = candle.GetDecimalOrDefault("v")
                    });
                }

                if (candleItems.Count > 0)
                {
                    var candlestick = new SCandle
                    {
                        exchange = ExchangeName,
                        symbol = symbol,
                        interval = interval,
                        timestamp = latestTimestamp,
                        result = candleItems
                    };

                    InvokeCandleCallback(candlestick);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Candle processing error: {ex.Message}");
            }
        }

        private async Task ProcessAccountData(JsonElement json)
        {
            try
            {
                if (!(json.TryGetArray("data", out var data)))
                    return;

                var balanceItems = new List<SBalanceItem>();
                var timestamp = TimeExtension.UnixTime;

                foreach (var account in data.EnumerateArray())
                {
                    var currency = account.GetStringOrDefault("ccy");
                    var free = account.GetDecimalOrDefault("availBal");
                    var used = account.GetDecimalOrDefault("frozenBal");
                    var total = account.GetDecimalOrDefault("bal");

                    if (total > 0 || free > 0 || used > 0)
                    {
                        balanceItems.Add(new SBalanceItem
                        {
                            currency = currency,
                            free = free,
                            used = used,
                            total = total,
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
            catch (Exception ex)
            {
                RaiseError($"Account processing error: {ex.Message}");
            }
        }

        private async Task ProcessOrderData(JsonElement json)
        {
            try
            {
                if (!(json.TryGetArray("data", out var data)))
                    return;

                var orderList = new List<SOrderItem>();
                var timestamp = TimeExtension.UnixTime;

                foreach (var order in data.EnumerateArray())
                {
                    orderList.Add(new SOrderItem
                    {
                        orderId = order.GetStringOrDefault("orderId"),
                        clientOrderId = order.GetStringOrDefault("clientOid"),
                        symbol = ParsingHelpers.NormalizeSymbol(order.GetStringOrDefault("instId")),
                        side = order.GetStringOrDefault("side") == "buy" ? OrderSide.Buy : OrderSide.Sell,
                        type = ParsingHelpers.ParseGenericOrderType(order.GetStringOrDefault("orderType")),
                        status = ParsingHelpers.ParseGenericOrderStatus(order.GetStringOrDefault("status")),
                        price = order.GetDecimalOrDefault("price"),
                        quantity = order.GetDecimalOrDefault("size"),
                        filledQuantity = order.GetDecimalOrDefault("fillSize"),
                        createTime = order.GetInt64OrDefault("cTime", timestamp),
                        updateTime = order.GetInt64OrDefault("uTime", timestamp)
                    });
                }

                if (orderList.Count > 0)
                {
                    var orders = new SOrder
                    {
                        exchange = ExchangeName,
                        timestamp = timestamp,
                        orders = orderList
                    };

                    InvokeOrderCallback(orders);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Order processing error: {ex.Message}");
            }
        }

        private async Task ProcessPositionData(JsonElement json)
        {
            try
            {
                if (!(json.TryGetArray("data", out var data)))
                    return;

                var positionList = new List<SPositionItem>();
                var timestamp = TimeExtension.UnixTime;

                foreach (var pos in data.EnumerateArray())
                {
                    positionList.Add(new SPositionItem
                    {
                        symbol = ParsingHelpers.NormalizeSymbol(pos.GetStringOrDefault("instId")),
                        side = pos.GetStringOrDefault("holdSide") == "long" ? PositionSide.Long : PositionSide.Short,
                        size = pos.GetDecimalOrDefault("total"),
                        entryPrice = pos.GetDecimalOrDefault("averageOpenPrice"),
                        markPrice = pos.GetDecimalOrDefault("markPrice"),
                        unrealizedPnl = pos.GetDecimalOrDefault("unrealizedPL"),
                        realizedPnl = pos.GetDecimalOrDefault("achievedProfits")
                    });
                }

                if (positionList.Count > 0)
                {
                    var positions = new SPosition
                    {
                        exchange = ExchangeName,
                        timestamp = timestamp,
                        positions = positionList
                    };

                    InvokePositionCallback(positions);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Position processing error: {ex.Message}");
            }
        }

        public override async Task<bool> SubscribeOrderbookAsync(string symbol)
        {
            try
            {
                var instId = ParsingHelpers.RemoveDelimiter(symbol);  // This removes the slash from BTC/USDT -> BTCUSDT
                var subscription = new
                {
                    op = "subscribe",
                    args = new[]
                    {
                        new
                        {
                            instType = "SPOT",
                            channel = "books",
                            instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                        }
                    }
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));

                MarkSubscriptionActive("orderbook", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Orderbook subscription error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeTradesAsync(string symbol)
        {
            try
            {
                var instId = ParsingHelpers.RemoveDelimiter(symbol);  // This removes the slash from BTC/USDT -> BTCUSDT
                var subscription = new
                {
                    op = "subscribe",
                    args = new[]
                    {
                        new
                        {
                            instType = "SPOT",
                            channel = "trade",
                            instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                        }
                    }
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));

                MarkSubscriptionActive("trades", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Trades subscription error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeTickerAsync(string symbol)
        {
            try
            {
                var instId = ParsingHelpers.RemoveDelimiter(symbol);  // This removes the slash from BTC/USDT -> BTCUSDT
                var subscription = new
                {
                    op = "subscribe",
                    args = new[]
                    {
                        new
                        {
                            instType = "SPOT",
                            channel = "ticker",
                            instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                        }
                    }
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));

                MarkSubscriptionActive("ticker", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Ticker subscription error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeCandlesAsync(string symbol, string interval)
        {
            try
            {
                var instId = ParsingHelpers.RemoveDelimiter(symbol);  // This removes the slash from BTC/USDT -> BTCUSDT
                var channelInterval = ParsingHelpers.ToBitgetChannelInterval(interval);

                var subscription = new
                {
                    op = "subscribe",
                    args = new[]
                    {
                        new
                        {
                            instType = "SPOT",
                            channel = $"candle{channelInterval}",
                            instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                        }
                    }
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));

                MarkSubscriptionActive($"candle{channelInterval}", symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Candles subscription error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> UnsubscribeAsync(string channel, string symbol)
        {
            try
            {
                var instId = ParsingHelpers.RemoveDelimiter(symbol);  // This removes the slash from BTC/USDT -> BTCUSDT
                var unsubscription = new
                {
                    op = "unsubscribe",
                    args = new[]
                    {
                        new
                        {
                            instType = "SPOT",
                            channel = channel,
                            instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                        }
                    }
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
            // V2 API: Bitget uses plain string "ping" instead of JSON object
            return "ping";
        }

        protected override string CreateAuthenticationMessage(string apiKey, string secretKey)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var method = "GET";
            var requestPath = "/user/verify";

            var sign = GenerateSignature(secretKey, timestamp, method, requestPath);

            return JsonSerializer.Serialize(new
            {
                op = "login",
                args = new[]
                {
                    new
                    {
                        apiKey = apiKey,
                        passphrase = "", // Bitget may require passphrase
                        timestamp = timestamp,
                        sign = sign
                    }
                }
            });
        }

        private string GenerateSignature(string secretKey, string timestamp, string method, string requestPath, string body = "")
        {
            var message = timestamp + method + requestPath + body;
            var encoding = new System.Text.ASCIIEncoding();
            byte[] keyByte = encoding.GetBytes(secretKey);
            byte[] messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new System.Security.Cryptography.HMACSHA256(keyByte))
            {
                byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hashmessage);
            }
        }

    // Note: per-symbol/interval/order-type/status conversion methods have been replaced by ExchangeParsingHelpers.

        #region Batch Subscription Support

        /// <summary>
        /// Bitget supports batch subscription - multiple args in single message
        /// </summary>
        protected override bool SupportsBatchSubscription()
        {
            return true;
        }

        /// <summary>
        /// Send batch subscriptions for Bitget - combines multiple channels/symbols in args array
        /// </summary>
        protected override async Task<bool> SendBatchSubscriptionsAsync(List<KeyValuePair<string, SubscriptionInfo>> subscriptions)
        {
            try
            {
                RaiseError($"Starting Bitget batch subscription for {subscriptions.Count} subscriptions");

                // Build list of all subscription args
                var args = new List<object>();

                foreach (var kvp in subscriptions)
                {
                    var subscription = kvp.Value;
                    var instId = ParsingHelpers.RemoveDelimiter(subscription.Symbol);  // This removes the slash from BTC/USDT -> BTCUSDT

                    // Map channel names to Bitget channel format
                    string channelName = subscription.Channel.ToLower() switch
                    {
                        "orderbook" or "depth" => "books",
                        "trades" or "trade" => "trade",
                        "ticker" => "ticker",
                        "candles" or "kline" or "candlestick" => !string.IsNullOrEmpty(subscription.Extra)
                            ? $"candle{ParsingHelpers.ToBitgetChannelInterval(subscription.Extra)}"
                            : "candle1m",
                        _ => subscription.Channel
                    };

                    // Create subscription arg object
                    var arg = new
                    {
                        instType = "SPOT",
                        channel = channelName,
                        instId = instId  // Bitget expects BTCUSDT format (no slash, no suffix)
                    };

                    args.Add(arg);
                }

                if (args.Count == 0)
                    return true;

                // Bitget allows multiple args in a single subscription message
                // Typical limit is around 100 subscriptions per connection
                const int maxArgsPerMessage = 100;

                if (args.Count <= maxArgsPerMessage)
                {
                    // Send all args in one message
                    var subscriptionMessage = new
                    {
                        op = "subscribe",
                        args = args.ToArray()
                    };

                    var json = JsonSerializer.Serialize(subscriptionMessage);
                    await SendMessageAsync(json);
                    RaiseError($"Sent Bitget batch subscription with {args.Count} channels");
                }
                else
                {
                    // Split into multiple messages if exceeding limit
                    var messageCount = (args.Count + maxArgsPerMessage - 1) / maxArgsPerMessage;

                    for (int i = 0; i < messageCount; i++)
                    {
                        var batch = args
                            .Skip(i * maxArgsPerMessage)
                            .Take(maxArgsPerMessage)
                            .ToArray();

                        var subscriptionMessage = new
                        {
                            op = "subscribe",
                            args = batch
                        };

                        await SendMessageAsync(JsonSerializer.Serialize(subscriptionMessage));
                        RaiseError($"Sent Bitget batch subscription {i + 1}/{messageCount} with {batch.Length} channels");

                        // Small delay between batches if multiple messages
                        if (i < messageCount - 1)
                            await Task.Delay(100);
                    }
                }

                RaiseError($"Completed Bitget batch subscription for {subscriptions.Count} total subscriptions");

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