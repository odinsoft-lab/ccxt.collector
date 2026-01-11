using CCXT.Collector.Service;
using System.Buffers;
using CCXT.Collector.Models.WebSocket;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCXT.Collector.Core.Abstractions
{
    /// <summary>
    /// Exchange operational status
    /// </summary>
    public enum ExchangeStatus
    {
        /// <summary>
        /// Exchange is fully operational
        /// </summary>
        Active,

        /// <summary>
        /// Exchange is undergoing maintenance
        /// </summary>
        Maintenance,

        /// <summary>
        /// Exchange is deprecated but still accessible
        /// </summary>
        Deprecated,

        /// <summary>
        /// Exchange has been permanently closed
        /// </summary>
        Closed,

        /// <summary>
        /// Exchange status is unknown
        /// </summary>
        Unknown
    }
    
    /// <summary>
    /// Base WebSocket client implementation
    /// </summary>
    public abstract class WebSocketClientBase : IWebSocketClient
    {
        protected ClientWebSocket _webSocket;
        protected ClientWebSocket _privateWebSocket; // For authenticated channels
        protected CancellationTokenSource _cancellationTokenSource;
        protected readonly SemaphoreSlim _sendSemaphore;
        protected Timer _pingTimer;
        protected int _reconnectAttempts = 0;
        protected readonly int _maxReconnectAttempts = 10;  // Increased from 5
        protected readonly int _reconnectDelayMs = 5000;

        // Diagnostics counters
        protected int _totalMessageFailures = 0;
        protected int _totalReconnects = 0; // successful reconnects (excludes initial connect)

        // Message parsing failure tracking
        protected int _consecutiveMessageFailures = 0;
        protected readonly int _maxConsecutiveMessageFailures = 5; // Default, can be overridden via environment variable
        protected bool _isReconnecting = false;

        // Subscription management
        protected readonly ConcurrentDictionary<string, SubscriptionInfo> _subscriptions;

        // Exchange rate support for multi-currency 
        protected decimal _exchangeRate = 1.0m;

        // Exchange status management
        protected ExchangeStatus _exchangeStatus = ExchangeStatus.Active;
        protected DateTime? _closedDate = null;
        protected string _statusMessage = null;
        protected List<string> _alternativeExchanges = new List<string>();

        // Authentication
        protected string _apiKey;
        protected string _secretKey;
        protected bool _isAuthenticated = false;

        // Observability
        protected IChannelObserver _channelObserver;
        protected DateTime? _connectedSince;

        public abstract string ExchangeName { get; }
        protected abstract string WebSocketUrl { get; }
        protected virtual string PrivateWebSocketUrl => WebSocketUrl; // Some exchanges use different URLs
        protected abstract int PingIntervalMs { get; }

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;
        public bool IsAuthenticated => _isAuthenticated && (_privateWebSocket?.State == WebSocketState.Open || IsConnected);

        // Diagnostics (read-only)
        public int ReconnectAttemptCount => _reconnectAttempts;
        public int TotalMessageFailures => _totalMessageFailures;
        public int TotalReconnects => _totalReconnects;

        // Exchange status properties
        public ExchangeStatus Status => _exchangeStatus;
        public DateTime? ClosedDate => _closedDate;
        public string StatusMessage => _statusMessage ?? GetDefaultStatusMessage();
        public List<string> AlternativeExchanges => _alternativeExchanges = new List<string>();
        public bool IsActive => _exchangeStatus == ExchangeStatus.Active;

        #region Events - Public Data

        public event Action<STicker> OnTickerReceived;
        public event Action<STrade> OnTradeReceived;
        public event Action<SOrderBook> OnOrderbookReceived;
        public event Action<SCandle> OnCandleReceived;

        #endregion

        #region Events - Private Data

        public event Action<SBalance> OnBalanceUpdate;
        public event Action<SOrder> OnOrderUpdate;
        public event Action<SPosition> OnPositionUpdate;

        #endregion

        #region Events - Connection

        public event Action OnConnected;
        public event Action OnAuthenticated;
        public event Action OnDisconnected;
        public event Action<string> OnError;
        public event Action<string> OnInfo; // For informational messages (non-error)
        public event Action OnPermanentFailure; // Fires when max reconnection attempts exceeded

        #endregion

        protected WebSocketClientBase()
        {
            _sendSemaphore = new SemaphoreSlim(1, 1);
            _subscriptions = new ConcurrentDictionary<string, SubscriptionInfo>();

            // Optional: override reconnection / failure thresholds via environment variables
            if (int.TryParse(Environment.GetEnvironmentVariable("CCXT_MAX_RECONNECT_ATTEMPTS"), out var maxRec) && maxRec > 0)
                _maxReconnectAttempts = maxRec;
            if (int.TryParse(Environment.GetEnvironmentVariable("CCXT_MAX_MSG_FAILURES"), out var maxFail) && maxFail > 0)
                _maxConsecutiveMessageFailures = maxFail;
        }

        /// <summary>
        /// Set the channel observer for metrics collection
        /// </summary>
        /// <param name="observer">Channel observer instance</param>
        public void SetChannelObserver(IChannelObserver observer)
        {
            _channelObserver = observer;
        }

        /// <summary>
        /// Get the current channel observer
        /// </summary>
        public IChannelObserver ChannelObserver => _channelObserver;

        /// <summary>
        /// Get connection health information
        /// </summary>
        public ConnectionHealth GetConnectionHealth()
        {
            return new ConnectionHealth
            {
                ExchangeName = ExchangeName,
                IsConnected = IsConnected,
                IsAuthenticated = IsAuthenticated,
                ReconnectAttempts = _reconnectAttempts,
                TotalReconnects = _totalReconnects,
                TotalMessageFailures = _totalMessageFailures,
                ConnectedSince = _connectedSince,
                ActiveSubscriptions = _subscriptions.Count(s => s.Value.IsActive)
            };
        }

        /// <summary>
        /// Get default status message based on current status
        /// </summary>
        protected virtual string GetDefaultStatusMessage()
        {
            return _exchangeStatus switch
            {
                ExchangeStatus.Active => $"{ExchangeName} exchange is fully operational.",
                ExchangeStatus.Maintenance => $"{ExchangeName} exchange is currently under maintenance. Please try again later.",
                ExchangeStatus.Deprecated => $"{ExchangeName} exchange is deprecated and will be removed in a future version.",
                ExchangeStatus.Closed => _closedDate.HasValue
                    ? $"{ExchangeName} exchange permanently closed on {_closedDate.Value:yyyy-MM-dd}."
                    : $"{ExchangeName} exchange is permanently closed.",
                ExchangeStatus.Unknown => $"{ExchangeName} exchange status is unknown.",
                _ => $"{ExchangeName} exchange status: {_exchangeStatus}"
            };
        }

        /// <summary>
        /// Set exchange status
        /// </summary>
        protected void SetExchangeStatus(ExchangeStatus status, string message = null, DateTime? closedDate = null, params string[] alternatives)
        {
            _exchangeStatus = status;
            _statusMessage = message;
            _closedDate = closedDate;
            if (alternatives != null && alternatives.Length > 0)
            {
                _alternativeExchanges.Clear();
                _alternativeExchanges.AddRange(alternatives);
            }
        }

        /// <summary>
        /// Add subscription for later batch processing
        /// </summary>
        public virtual void AddSubscription(string channel, string symbol, string interval = null)
        {
            var key = CreateSubscriptionKey(channel, symbol, interval);
            _subscriptions[key] = new SubscriptionInfo
            {
                Channel = channel,
                Symbol = symbol,
                SubscribedAt = DateTime.UtcNow,
                IsActive = false, // Not active until connected
                Extra = interval
            };
        }

        /// <summary>
        /// Add multiple subscriptions for batch processing
        /// </summary>
        public virtual void AddSubscriptions(List<(string channel, string symbol, string interval)> subscriptions)
        {
            foreach (var (channel, symbol, interval) in subscriptions)
            {
                AddSubscription(channel, symbol, interval);
            }
        }

        /// <summary>
        /// Connect to WebSocket and execute all pending subscriptions
        /// </summary>
        public virtual async Task<bool> ConnectAndSubscribeAsync()
        {
            try
            {
                // First connect to the WebSocket
                var connected = await ConnectAsync();
                if (!connected)
                {
                    RaiseError($"Failed to connect to {ExchangeName}");
                    return false;
                }

                // Get all subscriptions that are not yet active
                var pendingSubscriptions = _subscriptions
                    .Where(s => !s.Value.IsActive)
                    .ToList();

                if (pendingSubscriptions.Count == 0)
                    return true;

                // Check if exchange supports batch subscriptions
                if (SupportsBatchSubscription())
                {
                    // Send batch subscription for exchanges like Upbit
                    var result = await SendBatchSubscriptionsAsync(pendingSubscriptions);

                    if (result)
                    {
                        // Mark all subscriptions as active
                        foreach (var kvp in pendingSubscriptions)
                        {
                            kvp.Value.IsActive = true;
                        }
                    }

                    return result;
                }
                else
                {
                    // Send individual subscriptions for regular exchanges
                    var subscriptionTasks = new List<Task<bool>>();

                    foreach (var kvp in pendingSubscriptions)
                    {
                        var subscription = kvp.Value;

                        switch (subscription.Channel.ToLower())
                        {
                            case "orderbook":
                            case "depth":
                                subscriptionTasks.Add(SubscribeOrderbookAsync(subscription.Symbol));
                                break;
                            case "trades":
                            case "trade":
                                subscriptionTasks.Add(SubscribeTradesAsync(subscription.Symbol));
                                break;
                            case "ticker":
                                subscriptionTasks.Add(SubscribeTickerAsync(subscription.Symbol));
                                break;
                            case "candles":
                            case "kline":
                                if (!string.IsNullOrEmpty(subscription.Extra))
                                    subscriptionTasks.Add(SubscribeCandlesAsync(subscription.Symbol, subscription.Extra));
                                break;
                            default:
                                RaiseError($"Unknown channel type: {subscription.Channel}");
                                break;
                        }
                    }

                    // Wait for all subscriptions to complete
                    if (subscriptionTasks.Count > 0)
                    {
                        var results = await Task.WhenAll(subscriptionTasks);
                        var successCount = results.Count(r => r);

                        if (successCount == 0)
                        {
                            RaiseError($"All subscriptions failed for {ExchangeName}");
                            return false;
                        }
                        else if (successCount < results.Length)
                        {
                            RaiseError($"Some subscriptions failed: {successCount}/{results.Length} succeeded");
                        }

                        // Mark successful subscriptions as active
                        foreach (var kvp in pendingSubscriptions)
                        {
                            kvp.Value.IsActive = true;
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"ConnectAndSubscribe failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if exchange supports batch subscription (override in derived classes)
        /// </summary>
        protected virtual bool SupportsBatchSubscription()
        {
            return false; // Default: most exchanges send individual subscriptions
        }

        /// <summary>
        /// Send batch subscriptions (override in exchanges that support it like Upbit)
        /// </summary>
        protected virtual async Task<bool> SendBatchSubscriptionsAsync(List<KeyValuePair<string, SubscriptionInfo>> subscriptions)
        {
            // Default implementation: not supported
            RaiseError($"{ExchangeName} does not support batch subscriptions");
            return false;
        }

        public virtual async Task<bool> ConnectAsync()
        {
            try
            {
                // Check if exchange is active before attempting connection
                if (_exchangeStatus == ExchangeStatus.Closed)
                {
                    var errorMsg = $"Cannot connect to {ExchangeName}: {StatusMessage}";
                    if (_alternativeExchanges?.Count > 0)
                    {
                        errorMsg += $" Consider using: {string.Join(", ", _alternativeExchanges)}";
                    }
                    RaiseError(errorMsg);
                    return false;
                }

                if (_exchangeStatus == ExchangeStatus.Maintenance)
                {
                    RaiseError($"Cannot connect to {ExchangeName}: {StatusMessage}");
                    return false;
                }

                if (_exchangeStatus == ExchangeStatus.Deprecated)
                {
                    RaiseError($"Warning: {ExchangeName} is deprecated. {StatusMessage}");
                    // Continue with connection for deprecated exchanges
                }

                if (IsConnected)
                    return true;

                _cancellationTokenSource = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();

                // Configure WebSocket options
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                ConfigureWebSocket(_webSocket);

                await _webSocket.ConnectAsync(new Uri(WebSocketUrl), _cancellationTokenSource.Token);

                if (IsConnected)
                {
                    if (_reconnectAttempts > 0) // successful reconnect
                        _totalReconnects++;

                    _reconnectAttempts = 0;
                    RaiseConnected();

                    // Start receive loop
                    _ = Task.Run(() => ReceiveLoop(_webSocket, false));

                    // Start ping timer if needed
                    if (PingIntervalMs > 0)
                    {
                        _pingTimer = new Timer(async _ => await SendPingAsync(), null,
                            PingIntervalMs, PingIntervalMs);
                    }

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                RaiseError($"Connection failed: {ex.Message}");
                await HandleReconnectAsync();
                return false;
            }
        }

        public virtual async Task<bool> ConnectAsync(string apiKey, string secretKey)
        {
            _apiKey = apiKey;
            _secretKey = secretKey;

            // First connect to public channels
            var publicConnected = await ConnectAsync();
            if (!publicConnected)
                return false;

            // Then authenticate for private channels
            return await AuthenticateAsync();
        }

        protected virtual async Task<bool> AuthenticateAsync()
        {
            try
            {
                // Some exchanges use same connection for auth, others use separate
                if (RequiresSeparatePrivateConnection())
                {
                    _privateWebSocket = new ClientWebSocket();
                    ConfigureWebSocket(_privateWebSocket);
                    await _privateWebSocket.ConnectAsync(new Uri(PrivateWebSocketUrl), _cancellationTokenSource.Token);

                    // Start receive loop for private socket
                    _ = Task.Run(() => ReceiveLoop(_privateWebSocket, true));
                }

                // Send authentication message
                var authMessage = CreateAuthenticationMessage(_apiKey, _secretKey);
                if (!string.IsNullOrEmpty(authMessage))
                {
                    var socket = _privateWebSocket ?? _webSocket;
                    await SendMessageAsync(authMessage, socket);

                    // Wait for auth confirmation (simplified - real implementation would wait for response)
                    await Task.Delay(1000);

                    _isAuthenticated = true;
                    RaiseAuthenticated();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                RaiseError($"Authentication failed: {ex.Message}");
                return false;
            }
        }

        protected virtual bool RequiresSeparatePrivateConnection() => false;
        protected virtual string CreateAuthenticationMessage(string apiKey, string secretKey) => null;

        public virtual async Task DisconnectAsync()
        {
            try
            {
                _pingTimer?.Dispose();
                _cancellationTokenSource?.Cancel();

                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting", CancellationToken.None);
                }

                _webSocket?.Dispose();
                _webSocket = null;

                RaiseDisconnected();
            }
            catch (Exception ex)
            {
                RaiseError($"Disconnect error: {ex.Message}");
            }
        }

        protected virtual void ConfigureWebSocket(ClientWebSocket webSocket)
        {
            // Override in derived classes for exchange-specific configuration
        }

        protected virtual async Task ReceiveLoop(ClientWebSocket socket, bool isPrivate)
        {
            const int InitialSize = 16 * 1024; // 16KB
            const int MaxSize = 2 * 1024 * 1024; // 2MB upper bound
            int bufferSize = InitialSize;
            byte[] rented = ArrayPool<byte>.Shared.Rent(bufferSize);
            var buffer = new ArraySegment<byte>(rented, 0, bufferSize);
            var messageBuilder = new StringBuilder();
            var binaryData = new List<byte>();

            try
            {
                while (socket?.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();
                    binaryData.Clear();

                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(rented, 0, bufferSize), _cancellationTokenSource.Token);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            messageBuilder.Append(Encoding.UTF8.GetString(rented, 0, result.Count));
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            binaryData.AddRange(rented.AsSpan(0, result.Count).ToArray());
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await HandleDisconnectAsync();
                            return;
                        }

                        // Expand buffer if needed (message not complete and buffer full)
                        if (!result.EndOfMessage && result.Count == bufferSize)
                        {
                            int newSize = Math.Min(bufferSize * 2, MaxSize);
                            if (newSize > bufferSize)
                            {
                                byte[] newRent = ArrayPool<byte>.Shared.Rent(newSize);
                                Buffer.BlockCopy(rented, 0, newRent, 0, bufferSize);
                                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
                                rented = newRent;
                                bufferSize = newSize;
                            }
                        }
                    }
                    while (!result.EndOfMessage);

                    if (messageBuilder.Length > 0)
                    {
                        await ProcessSingleMessageSafeAsync(messageBuilder.ToString(), isPrivate);
                    }
                    else if (binaryData.Count > 0)
                    {
                        var message = Encoding.UTF8.GetString(binaryData.ToArray());
                        await ProcessSingleMessageSafeAsync(message, isPrivate);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Receive error: {ex.Message}");
                await HandleReconnectAsync();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
        }

        /// <summary>
        /// Parse a single message with exception isolation and cumulative failure tracking.
        /// </summary>
        private async Task ProcessSingleMessageSafeAsync(string message, bool isPrivate)
        {
            try
            {
                await ProcessMessageAsync(message, isPrivate);
                if (_consecutiveMessageFailures > 0)
                    _consecutiveMessageFailures = 0;
            }
            catch (Exception ex)
            {
                _consecutiveMessageFailures++;
                _totalMessageFailures++;

                RaiseError($"Message processing error ({_consecutiveMessageFailures}/{_maxConsecutiveMessageFailures}): {ex.Message}");

                if (_consecutiveMessageFailures >= _maxConsecutiveMessageFailures)
                {
                    _consecutiveMessageFailures = 0; // reset before reconnect
                    await HandleReconnectAsync();
                }
            }
        }

        protected abstract Task ProcessMessageAsync(string message, bool isPrivate = false);

        protected async Task SendMessageAsync(string message, ClientWebSocket socket = null)
        {
            socket ??= _webSocket;

            if (socket?.State != WebSocketState.Open)
            {
                RaiseError("Cannot send message: Not connected");
                return;
            }

            await _sendSemaphore.WaitAsync();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(bytes),
                    System.Net.WebSockets.WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                RaiseError($"Send error: {ex.Message}");
                await HandleReconnectAsync();
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        protected virtual async Task SendPingAsync()
        {
            try
            {
                if (IsConnected)
                {
                    var pingMessage = CreatePingMessage();
                    if (!string.IsNullOrEmpty(pingMessage))
                    {
                        await SendMessageAsync(pingMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Ping error: {ex.Message}");
            }
        }

        protected virtual string CreatePingMessage()
        {
            // Override in derived classes
            return null;
        }

        protected async Task HandleDisconnectAsync()
        {
            RaiseDisconnected();
            await HandleReconnectAsync();
        }

        protected async Task HandleReconnectAsync()
        {
            if (_isReconnecting)
                return;
            _isReconnecting = true;
            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                RaiseError($"Max reconnection attempts ({_maxReconnectAttempts}) reached");
                RaisePermanentFailure();
                _isReconnecting = false;
                return;
            }

            _reconnectAttempts++;

            // Exponential backoff with cap + jitter(±30%)
            var baseDelay = Math.Min(_reconnectDelayMs * _reconnectAttempts, 60000);  // Cap at 60 seconds
            var jitterFactor = 0.3; // 30%
            var rand = new Random();
            var jitter = 1.0 + (rand.NextDouble() * 2 - 1) * jitterFactor; // 0.7 ~ 1.3

            var delay = (int)(baseDelay * jitter);
            RaiseError($"Reconnecting in {delay}ms (base {baseDelay}ms, attempt {_reconnectAttempts}/{_maxReconnectAttempts})...");

            await Task.Delay(delay);

            var connected = await ConnectAsync();
            if (connected)
            {
                // On successful reconnect restore previously active subscriptions
                await RestoreActiveSubscriptionsAsync();
            }
            _isReconnecting = false;
        }

        #region Abstract Methods for Public Subscriptions

        public abstract Task<bool> SubscribeTickerAsync(string symbol);
        public abstract Task<bool> SubscribeOrderbookAsync(string symbol);
        public abstract Task<bool> SubscribeTradesAsync(string symbol);
        public abstract Task<bool> SubscribeCandlesAsync(string symbol, string interval);
        public abstract Task<bool> UnsubscribeAsync(string channel, string symbol);

        // Market-based overloads - virtual with default implementation for backward compatibility
        public virtual Task<bool> SubscribeOrderbookAsync(Market market)
        {
            return SubscribeOrderbookAsync(market.ToString());
        }

        public virtual Task<bool> SubscribeTradesAsync(Market market)
        {
            return SubscribeTradesAsync(market.ToString());
        }

        public virtual Task<bool> SubscribeTickerAsync(Market market)
        {
            return SubscribeTickerAsync(market.ToString());
        }

        public virtual Task<bool> SubscribeCandlesAsync(Market market, string interval)
        {
            return SubscribeCandlesAsync(market.ToString(), interval);
        }

        public virtual Task<bool> UnsubscribeAsync(string channel, Market market)
        {
            return UnsubscribeAsync(channel, market.ToString());
        }

        // Virtual method for exchange-specific symbol formatting - default implementation uses ToString()
        protected virtual string FormatSymbol(Market market)
        {
            return market.ToString();
        }

        // Set exchange rate for currency conversion (useful for KRW/USD conversions)
        public virtual void SetExchangeRate(decimal rate)
        {
            _exchangeRate = rate;
        }

        #endregion

        #region Virtual Methods for Private Subscriptions

        public virtual async Task<bool> SubscribeBalanceAsync()
        {
            if (!IsAuthenticated)
            {
                RaiseError("Not authenticated. Please connect with API credentials.");
                return false;
            }

            // Override in derived classes
            return await Task.FromResult(false);
        }

        public virtual async Task<bool> SubscribeOrdersAsync()
        {
            if (!IsAuthenticated)
            {
                RaiseError("Not authenticated. Please connect with API credentials.");
                return false;
            }

            // Override in derived classes
            return await Task.FromResult(false);
        }

        public virtual async Task<bool> SubscribePositionsAsync()
        {
            if (!IsAuthenticated)
            {
                RaiseError("Not authenticated. Please connect with API credentials.");
                return false;
            }

            // Override in derived classes for futures exchanges
            return await Task.FromResult(false);
        }

        #endregion

        #region Protected Helper Methods

        // Public data callbacks
        protected void InvokeTickerCallback(STicker ticker)
        {
            OnTickerReceived?.Invoke(ticker);
        }

        protected void InvokeTradeCallback(STrade trade)
        {
            OnTradeReceived?.Invoke(trade);
        }

        protected void InvokeOrderbookCallback(SOrderBook orderbook)
        {
            OnOrderbookReceived?.Invoke(orderbook);
        }

        // Event raising helper methods for derived classes
        protected virtual void RaiseError(string message)
        {
            _channelObserver?.OnError(ExchangeName, message);
            OnError?.Invoke(message);
        }

        protected virtual void RaiseInfo(string message)
        {
            OnInfo?.Invoke(message);
        }

        protected virtual void RaiseConnected()
        {
            _connectedSince = DateTime.UtcNow;
            _channelObserver?.OnConnectionStateChanged(ExchangeName, true);
            OnConnected?.Invoke();
        }

        protected virtual void RaiseAuthenticated()
        {
            OnAuthenticated?.Invoke();
        }

        protected virtual void RaiseDisconnected()
        {
            _channelObserver?.OnConnectionStateChanged(ExchangeName, false);
            OnDisconnected?.Invoke();
        }

        protected virtual void RaisePermanentFailure()
        {
            OnPermanentFailure?.Invoke();
        }

        protected void InvokeCandleCallback(SCandle candle)
        {
            OnCandleReceived?.Invoke(candle);
        }

        // Private data callbacks
        protected void InvokeBalanceCallback(SBalance balance)
        {
            OnBalanceUpdate?.Invoke(balance);
        }

        protected void InvokeOrderCallback(SOrder orders)
        {
            OnOrderUpdate?.Invoke(orders);
        }

        protected void InvokePositionCallback(SPosition positions)
        {
            OnPositionUpdate?.Invoke(positions);
        }

        #endregion

        /// <summary>
        /// Record message metrics for observability
        /// </summary>
        /// <param name="channel">Channel name (ticker, orderbook, trades, etc.)</param>
        /// <param name="symbol">Symbol</param>
        /// <param name="messageSize">Message size in bytes</param>
        /// <param name="processingTimeMs">Processing time in milliseconds</param>
        protected void RecordMessageMetrics(string channel, string symbol, int messageSize, double processingTimeMs)
        {
            _channelObserver?.OnMessageReceived(ExchangeName, channel, symbol, messageSize, processingTimeMs);
        }

        // Helper method to create a subscription key
        protected string CreateSubscriptionKey(string channel, string symbol)
        {
            return $"{channel}:{symbol}";
        }

        // Overloaded method to create a subscription key with interval
        protected string CreateSubscriptionKey(string channel, string symbol, string interval)
        {
            return string.IsNullOrEmpty(interval)
                ? $"{channel}:{symbol}"
                : $"{channel}:{symbol}:{interval}";
        }

        // Virtual method for resubscribing (can be overridden by derived classes)
        protected virtual async Task ResubscribeAsync(SubscriptionInfo subscription)
        {
            // Default implementation: call standard Subscribe* methods depending on channel type
            // Derived classes may override or call base then add extra logic if needed
            if (subscription == null)
                return;

            try
            {
                var channel = subscription.Channel?.ToLowerInvariant();
                var symbol = subscription.Symbol;
                var interval = subscription.Extra;

                if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(symbol))
                    return;

                bool ok = false;
                switch (channel)
                {
                    case "orderbook":
                    case "depth":
                        ok = await SubscribeOrderbookAsync(symbol);
                        break;
                    case "trades":
                    case "trade":
                        ok = await SubscribeTradesAsync(symbol);
                        break;
                    case "ticker":
                        ok = await SubscribeTickerAsync(symbol);
                        break;
                    case "candles":
                    case "kline":
                        if (!string.IsNullOrEmpty(interval))
                            ok = await SubscribeCandlesAsync(symbol, interval);
                        break;
                    default:
                        // Normalize key if some implementations stored "candles:1m" form
                        if (channel.StartsWith("candles:") || channel.StartsWith("kline:"))
                        {
                            var parts = channel.Split(':');
                            if (parts.Length == 2)
                            {
                                interval ??= parts[1];
                                if (!string.IsNullOrEmpty(interval))
                                    ok = await SubscribeCandlesAsync(symbol, interval);
                            }
                        }
                        break;
                }

                if (!ok)
                {
                    RaiseError($"Resubscribe failed (channel={subscription.Channel}, symbol={symbol})");
                }
                else
                {
                    MarkSubscriptionActive(subscription.Channel, subscription.Symbol, subscription.Extra);
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Resubscribe exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore subscriptions that were active prior to reconnection.
        /// Iterate IsActive=true entries in _subscriptions and re-subscribe by channel type.
        /// Failed items are logged and processing continues (partial success allowed).
        /// </summary>
        protected virtual async Task RestoreActiveSubscriptionsAsync()
        {
            try
            {
                if (_subscriptions == null || _subscriptions.Count == 0)
                    return;

                var snapshot = _subscriptions.Values
                    .Where(s => s.IsActive) // Only restore those that were previously active
                    .ToList();

                if (snapshot.Count == 0)
                    return;

                RaiseError($"Restoring {snapshot.Count} subscriptions after reconnect...");

                foreach (var sub in snapshot)
                {
                    try
                    {
                        await ResubscribeAsync(sub);
                    }
                    catch (Exception ex)
                    {
                        RaiseError($"Resubscribe failed: {sub.Channel}:{sub.Symbol} - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError($"RestoreActiveSubscriptions failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Marks a subscription as active and updates its timestamp when a subscribe succeeds.
        /// Exposed so exchange-specific Subscribe* implementations can call it after a successful subscribe.
        /// </summary>
        protected void MarkSubscriptionActive(string channel, string symbol, string extra = null)
        {
            try
            {
                var key = string.IsNullOrEmpty(extra) ? CreateSubscriptionKey(channel, symbol) : CreateSubscriptionKey(channel, symbol, extra);
                if (_subscriptions.TryGetValue(key, out var info))
                {
                    info.IsActive = true;
                    info.SubscribedAt = DateTime.UtcNow;
                }
                else
                {
                    // If not present, add a new entry (edge case)
                    _subscriptions[key] = new SubscriptionInfo
                    {
                        Channel = channel,
                        Symbol = symbol,
                        Extra = extra,
                        IsActive = true,
                        SubscribedAt = DateTime.UtcNow
                    };
                }

                // Notify observer of subscription change
                _channelObserver?.OnSubscriptionChanged(ExchangeName, channel, symbol, true);
            }
            catch (Exception ex)
            {
                RaiseError($"MarkSubscriptionActive error: {ex.Message}");
            }
        }

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _ = DisconnectAsync().ConfigureAwait(false);
                    _sendSemaphore?.Dispose();
                    _cancellationTokenSource?.Dispose();
                    _pingTimer?.Dispose();
                    _privateWebSocket?.Dispose();
                }
                // Free unmanaged resources (none in this class)
                _disposed = true;
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}