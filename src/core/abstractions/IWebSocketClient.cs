using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCXT.Collector.Service;

namespace CCXT.Collector.Core.Abstractions
{
    /// <summary>
    /// WebSocket client interface for exchange connections
    /// </summary>
    public interface IWebSocketClient : IDisposable
    {
        /// <summary>
        /// Exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Connection status
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Authenticated connection status
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Add subscription for later batch processing
        /// </summary>
        void AddSubscription(string channel, string symbol, string interval = null);

        /// <summary>
        /// Add multiple subscriptions for batch processing
        /// </summary>
        void AddSubscriptions(List<(string channel, string symbol, string interval)> subscriptions);

        /// <summary>
        /// Connect to WebSocket and execute all pending subscriptions
        /// </summary>
        Task<bool> ConnectAndSubscribeAsync();

        /// <summary>
        /// Connect to WebSocket (public channels)
        /// </summary>
        Task<bool> ConnectAsync();

        /// <summary>
        /// Connect to WebSocket with authentication (private channels)
        /// </summary>
        Task<bool> ConnectAsync(string apiKey, string secretKey);

        /// <summary>
        /// Disconnect from WebSocket
        /// </summary>
        Task DisconnectAsync();

        #region Public Channel Subscriptions

        /// <summary>
        /// Subscribe to ticker channel
        /// </summary>
        Task<bool> SubscribeTickerAsync(string symbol);

        /// <summary>
        /// Subscribe to orderbook/depth channel 
        /// </summary>
        Task<bool> SubscribeOrderbookAsync(string symbol);

        /// <summary>
        /// Subscribe to trades channel 
        /// </summary>
        Task<bool> SubscribeTradesAsync(string symbol);

        /// <summary>
        /// Subscribe to candlestick/kline channel 
        /// </summary>
        Task<bool> SubscribeCandlesAsync(string symbol, string interval);

        #endregion

        #region Private Channel Subscriptions

        /// <summary>
        /// Subscribe to account balance updates 
        /// </summary>
        Task<bool> SubscribeBalanceAsync();

        /// <summary>
        /// Subscribe to order updates 
        /// </summary>
        Task<bool> SubscribeOrdersAsync();

        /// <summary>
        /// Subscribe to position updates (for futures)
        /// </summary>
        Task<bool> SubscribePositionsAsync();

        #endregion

        /// <summary>
        /// Unsubscribe from channel
        /// </summary>
        Task<bool> UnsubscribeAsync(string channel, string symbol);

        #region Callback Events - Public Data

        /// <summary>
        /// 1. Ticker/Trade data received callback 
        /// </summary>
        event Action<STicker> OnTickerReceived;

        /// <summary>
        /// Trade execution data received callback 
        /// </summary>
        event Action<STrade> OnTradeReceived;

        /// <summary>
        /// 2. Orderbook/Depth data received callback 
        /// </summary>
        event Action<SOrderBook> OnOrderbookReceived;

        /// <summary>
        /// 3. Candlestick/K-Line data received callback 
        /// </summary>
        event Action<SCandle> OnCandleReceived;

        #endregion

        #region Callback Events - Private Data

        /// <summary>
        /// 4. Account balance update callback 
        /// </summary>
        event Action<SBalance> OnBalanceUpdate;

        /// <summary>
        /// 4. Order update callback 
        /// </summary>
        event Action<SOrder> OnOrderUpdate;

        /// <summary>
        /// 4. Position update callback (futures)
        /// </summary>
        event Action<SPosition> OnPositionUpdate;

        #endregion

        #region Connection Events

        /// <summary>
        /// Connection opened callback
        /// </summary>
        event Action OnConnected;

        /// <summary>
        /// Authentication success callback
        /// </summary>
        event Action OnAuthenticated;

        /// <summary>
        /// Connection closed callback
        /// </summary>
        event Action OnDisconnected;

        /// <summary>
        /// Error occurred callback
        /// </summary>
        event Action<string> OnError;

        /// <summary>
        /// Informational message callback (non-error)
        /// </summary>
        event Action<string> OnInfo;

        #endregion
    }

    /// <summary>
    /// WebSocket message types
    /// </summary>
    public enum CustomWebSocketMessageType
    {
        Subscribe,
        Unsubscribe,
        Ping,
        Pong,
        Data,
        Error,
        Info
    }
}
