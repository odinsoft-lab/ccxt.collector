using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Library;
using CCXT.Collector.Models.WebSocket;
using CCXT.Collector.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CCXT.Collector.Kucoin
{
    /*
     * KuCoin Support Markets: USDT, BTC, ETH, KCS
     *
     * API Documentation:
     *     https://docs.kucoin.com/
     *
     * WebSocket API:
     *     https://www.kucoin.com/docs/websocket/basic-info/create-connection
     *     https://www.kucoin.com/docs/websocket/basic-info/ping
     *     https://www.kucoin.com/docs/websocket/basic-info/subscribe/introduction
     *     https://www.kucoin.com/docs/websocket/spot-trading/public-channels/level2-5-best-ask-bid-orders
     *     https://www.kucoin.com/docs/websocket/spot-trading/public-channels/match
     *     https://www.kucoin.com/docs/websocket/spot-trading/public-channels/ticker
     *
     * Fees:
     *     https://www.kucoin.com/vip/level
     */
    
    /// <summary>
    /// KuCoin WebSocket client for real-time data streaming
    /// </summary>
    public class KucoinWebSocketClient : WebSocketClientBase
    {
        private readonly Dictionary<string, SOrderBook> _orderbookCache;
        private readonly HttpClient _httpClient;
        private string _connectId;
        private long _lastPingTime;
        private bool _welcomeReceived;
        private string _wsToken;
        private string _wsEndpoint;

        public override string ExchangeName => "Kucoin";
        
        // We'll set this dynamically after getting the token
        protected override string WebSocketUrl => _wsEndpoint ?? "wss://ws-api-spot.kucoin.com/";
        protected override int PingIntervalMs => 0; // We handle ping manually in PingLoop

        public KucoinWebSocketClient()
        {
            _orderbookCache = new Dictionary<string, SOrderBook>();
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.kucoin.com");
        }

        public override async Task<bool> ConnectAsync()
        {
            try
            {
                // First, get WebSocket connection details from REST API
                if (!await GetWebSocketEndpoint())
                {
                    RaiseError("Failed to get WebSocket endpoint");
                    return false;
                }

                // Connect to WebSocket with the token
                var success = await base.ConnectAsync();
                
                if (success)
                {
                    // Wait for welcome message
                    var timeout = DateTime.UtcNow.AddSeconds(5);
                    while (!_welcomeReceived && DateTime.UtcNow < timeout)
                    {
                        await Task.Delay(100);
                    }

                    if (!_welcomeReceived)
                    {
                        RaiseError("Welcome message not received");
                        return false;
                    }

                    // Start ping task
                    _ = Task.Run(PingLoop);
                }

                return success;
            }
            catch (Exception ex)
            {
                RaiseError($"Connection error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> GetWebSocketEndpoint()
        {
            try
            {
                // Get public token for WebSocket connection
                var response = await _httpClient.PostAsync("/api/v1/bullet-public", null);
                
                if (!response.IsSuccessStatusCode)
                {
                    RaiseError($"Failed to get WebSocket token: {response.StatusCode}");
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.GetStringOrDefault("code") == "200000")
                {
                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("token", out var token))
                        {
                            _wsToken = token.GetString();
                        }

                        if (data.TryGetArray("instanceServers", out var servers))
                        {
                            var server = servers[0];
                            if (server.TryGetProperty("endpoint", out var endpoint))
                            {
                                _wsEndpoint = $"{endpoint.GetString()}?token={_wsToken}";
                                return true;
                            }
                        }
                    }
                }

                RaiseError("Invalid response format from bullet-public API");
                return false;
            }
            catch (Exception ex)
            {
                RaiseError($"Error getting WebSocket endpoint: {ex.Message}");
                return false;
            }
        }

        private async Task PingLoop()
        {
            while (IsConnected)
            {
                try
                {
                    await Task.Delay(30000); // Ping every 30 seconds
                    
                    if (IsConnected)
                    {
                        var pingMessage = new
                        {
                            id = TimeExtension.UnixTime.ToString(),
                            type = "ping"
                        };
                        
                        await SendMessageAsync(JsonSerializer.Serialize(pingMessage));
                        _lastPingTime = TimeExtension.UnixTime;
                    }
                }
                catch (Exception ex)
                {
                    RaiseError($"Ping error: {ex.Message}");
                }
            }
        }

        protected override async Task ProcessMessageAsync(string message, bool isPrivate = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                var json = doc.RootElement;

                // Check message type
                if (json.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();
                    
                    switch (messageType)
                    {
                        case "welcome":
                            HandleWelcomeMessage(json);
                            break;
                            
                        case "pong":
                            // Pong received, connection is alive
                            break;
                            
                        case "ack":
                            // Subscription acknowledgment
                            HandleAckMessage(json);
                            break;
                            
                        case "message":
                            await HandleDataMessage(json);
                            break;
                            
                        case "error":
                            HandleErrorMessage(json);
                            break;
                            
                        default:
                            RaiseError($"Unknown message type: {messageType}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Message processing error: {ex.Message}");
            }
        }

        private void HandleWelcomeMessage(JsonElement json)
        {
            if (json.TryGetProperty("id", out var id))
            {
                _connectId = id.GetString();
                _welcomeReceived = true;
                RaiseInfo($"Connected with ID: {_connectId}");
            }
        }

        private void HandleAckMessage(JsonElement json)
        {
            if (json.TryGetProperty("id", out var id))
            {
                RaiseInfo($"Subscription acknowledged: {id}");
            }
        }

        private void HandleErrorMessage(JsonElement json)
        {
            var errorMsg = "Unknown error";
            
            if (json.TryGetProperty("data", out var data))
            {
                errorMsg = data.GetString();
            }
            
            RaiseError($"Server error: {errorMsg}");
        }

        private async Task HandleDataMessage(JsonElement json)
        {
            if (!json.TryGetProperty("topic", out var topicElement))
                return;

            var topic = topicElement.GetString();
            
            if (!json.TryGetProperty("data", out var data))
                return;

            // Parse topic to determine channel type
            if (topic.Contains("/spotMarket/level2Depth5:"))
            {
                HandleOrderbookUpdate(topic, data);
            }
            else if (topic.Contains("/market/match:"))
            {
                HandleTradeUpdate(topic, data);
            }
            else if (topic.Contains("/market/ticker:"))
            {
                HandleTickerUpdate(topic, data);
            }
        }

        private void HandleOrderbookUpdate(string topic, JsonElement data)
        {
            try
            {
                // Extract symbol from topic
                var symbol = ExtractSymbolFromTopic(topic);
                if (string.IsNullOrEmpty(symbol))
                    return;

                var orderbook = new SOrderBook
                {
                    exchange = ExchangeName,
                    symbol = ConvertSymbol(symbol),
                    stream = "orderbook",
                    sequentialId = data.TryGetProperty("timestamp", out var ts) ? 
                        ts.GetInt64() : TimeExtension.UnixTime,
                    timestamp = TimeExtension.UnixTime,
                    result = new SOrderBookData()
                };

                // Parse asks
                if (data.TryGetProperty("asks", out var asks))
                {
                    var askList = new List<SOrderBookItem>();
                    foreach (var ask in asks.EnumerateArray())
                    {
                        if (ask.GetArrayLength() >= 2)
                        {
                            askList.Add(new SOrderBookItem
                            {
                                price = decimal.Parse(ask[0].GetString()),
                                quantity = decimal.Parse(ask[1].GetString())
                            });
                        }
                    }
                    orderbook.result.asks = askList;
                }

                // Parse bids
                if (data.TryGetProperty("bids", out var bids))
                {
                    var bidList = new List<SOrderBookItem>();
                    foreach (var bid in bids.EnumerateArray())
                    {
                        if (bid.GetArrayLength() >= 2)
                        {
                            bidList.Add(new SOrderBookItem
                            {
                                price = decimal.Parse(bid[0].GetString()),
                                quantity = decimal.Parse(bid[1].GetString())
                            });
                        }
                    }
                    orderbook.result.bids = bidList;
                }

                InvokeOrderbookCallback(orderbook);
            }
            catch (Exception ex)
            {
                RaiseError($"Orderbook processing error: {ex.Message}");
            }
        }

        private void HandleTradeUpdate(string topic, JsonElement data)
        {
            try
            {
                var symbol = ExtractSymbolFromTopic(topic);
                if (string.IsNullOrEmpty(symbol))
                    return;

                var trade = new STrade
                {
                    exchange = ExchangeName,
                    symbol = ConvertSymbol(symbol),
                    stream = "trades",
                    result = new List<STradeItem>()
                };

                // KuCoin sends individual trade updates
                var tradeItem = new STradeItem
                {
                    timestamp = data.TryGetProperty("time", out var time) ? 
                        long.Parse(time.GetString()) / 1000000 : // Convert nanoseconds string to milliseconds
                        TimeExtension.UnixTime,
                    tradeId = data.TryGetProperty("sequence", out var seq) ? 
                        seq.GetString() : Guid.NewGuid().ToString(),
                    price = data.TryGetProperty("price", out var price) ? 
                        decimal.Parse(price.GetString()) : 0,
                    quantity = data.TryGetProperty("size", out var size) ? 
                        decimal.Parse(size.GetString()) : 0,
                    sideType = data.TryGetProperty("side", out var side) ? 
                        (side.GetString() == "buy" ? SideType.Bid : SideType.Ask) : SideType.Unknown
                };

                trade.result.Add(tradeItem);
                InvokeTradeCallback(trade);
            }
            catch (Exception ex)
            {
                RaiseError($"Trade processing error: {ex.Message}");
            }
        }

        private void HandleTickerUpdate(string topic, JsonElement data)
        {
            try
            {
                var symbol = ExtractSymbolFromTopic(topic);
                if (string.IsNullOrEmpty(symbol))
                    return;

                var ticker = new STicker
                {
                    exchange = ExchangeName,
                    symbol = ConvertSymbol(symbol),
                    timestamp = TimeExtension.UnixTime,
                    result = new STickerItem
                    {
                        timestamp = TimeExtension.UnixTime,
                        closePrice = data.TryGetProperty("price", out var price) ? 
                            decimal.Parse(price.GetString()) : 0,
                        bidPrice = data.TryGetProperty("bestBid", out var bid) ? 
                            decimal.Parse(bid.GetString()) : 0,
                        askPrice = data.TryGetProperty("bestAsk", out var ask) ? 
                            decimal.Parse(ask.GetString()) : 0,
                        volume = data.TryGetProperty("size", out var vol) ? 
                            decimal.Parse(vol.GetString()) : 0
                    }
                };

                InvokeTickerCallback(ticker);
            }
            catch (Exception ex)
            {
                RaiseError($"Ticker processing error: {ex.Message}");
            }
        }

        private string ExtractSymbolFromTopic(string topic)
        {
            // Topic format: /spotMarket/level2Depth5:BTC-USDT
            var parts = topic.Split(':');
            return parts.Length > 1 ? parts[1] : null;
        }

        private string ConvertSymbol(string kucoinSymbol)
        {
            // KuCoin uses BTC-USDT format, convert to BTC/USDT
            return kucoinSymbol?.Replace("-", "/");
        }

        private string ConvertSymbolToKucoin(string symbol)
        {
            // Convert BTC/USDT to BTC-USDT
            return symbol?.Replace("/", "-");
        }

        public override async Task<bool> SubscribeOrderbookAsync(string symbol)
        {
            try
            {
                var kucoinSymbol = ConvertSymbolToKucoin(symbol);
                var subscription = new
                {
                    id = TimeExtension.UnixTime,
                    type = "subscribe",
                    topic = $"/spotMarket/level2Depth5:{kucoinSymbol}",
                    privateChannel = false,
                    response = true
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
                var kucoinSymbol = ConvertSymbolToKucoin(symbol);
                var subscription = new
                {
                    id = TimeExtension.UnixTime,
                    type = "subscribe",
                    topic = $"/market/match:{kucoinSymbol}",
                    privateChannel = false,
                    response = true
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
                var kucoinSymbol = ConvertSymbolToKucoin(symbol);
                var subscription = new
                {
                    id = TimeExtension.UnixTime,
                    type = "subscribe",
                    topic = $"/market/ticker:{kucoinSymbol}",
                    privateChannel = false,
                    response = true
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                MarkSubscriptionActive("ticker", symbol);

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
                var kucoinSymbol = ConvertSymbolToKucoin(symbol);
                string topic = channel switch
                {
                    "orderbook" => $"/spotMarket/level2Depth5:{kucoinSymbol}",
                    "trades" => $"/market/match:{kucoinSymbol}",
                    "ticker" => $"/market/ticker:{kucoinSymbol}",
                    _ => null
                };

                if (topic == null)
                {
                    RaiseError($"Unknown channel: {channel}");
                    return false;
                }

                var unsubscription = new
                {
                    id = TimeExtension.UnixTime,
                    type = "unsubscribe",
                    topic = topic,
                    privateChannel = false,
                    response = true
                };

                await SendMessageAsync(JsonSerializer.Serialize(unsubscription));
                MarkSubscriptionInactive(channel, symbol);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Unsubscribe error: {ex.Message}");
                return false;
            }
        }

        public override async Task<bool> SubscribeCandlesAsync(string symbol, string interval)
        {
            try
            {
                var kucoinSymbol = ConvertSymbolToKucoin(symbol);
                
                // Convert interval to KuCoin format
                var kucoinInterval = ConvertIntervalToKucoin(interval);
                
                var subscription = new
                {
                    id = TimeExtension.UnixTime,
                    type = "subscribe",
                    topic = $"/market/candles:{kucoinSymbol}_{kucoinInterval}",
                    privateChannel = false,
                    response = true
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                MarkSubscriptionActive("candles", symbol, interval);

                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"Subscribe candles error: {ex.Message}");
                return false;
            }
        }

        private string ConvertIntervalToKucoin(string interval)
        {
            // Convert common interval formats to KuCoin format
            // KuCoin uses: 1min, 3min, 5min, 15min, 30min, 1hour, 2hour, 4hour, 6hour, 8hour, 12hour, 1day, 1week
            return interval switch
            {
                "1m" => "1min",
                "3m" => "3min",
                "5m" => "5min",
                "15m" => "15min",
                "30m" => "30min",
                "1h" => "1hour",
                "2h" => "2hour",
                "4h" => "4hour",
                "6h" => "6hour",
                "8h" => "8hour",
                "12h" => "12hour",
                "1d" => "1day",
                "1w" => "1week",
                _ => interval // Return as-is if not mapped
            };
        }

        public new void Dispose()
        {
            _httpClient?.Dispose();
            base.Dispose();
        }

        protected void MarkSubscriptionInactive(string channel, string symbol, string extra = null)
        {
            try
            {
                var key = string.IsNullOrEmpty(extra) ? CreateSubscriptionKey(channel, symbol) : CreateSubscriptionKey(channel, symbol, extra);
                if (_subscriptions.TryGetValue(key, out var info))
                {
                    info.IsActive = false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"MarkSubscriptionInactive error: {ex.Message}");
            }
        }
    }
}