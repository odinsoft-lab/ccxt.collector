using System;

namespace CCXT.Collector.Core.Abstractions
{
    /// <summary>
    /// Channel statistics data for observability
    /// </summary>
    public class ChannelStatistics
    {
        /// <summary>
        /// Exchange name
        /// </summary>
        public string ExchangeName { get; set; }

        /// <summary>
        /// Channel name (ticker, orderbook, trades, etc.)
        /// </summary>
        public string Channel { get; set; }

        /// <summary>
        /// Symbol being tracked
        /// </summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Total messages received
        /// </summary>
        public long MessageCount { get; set; }

        /// <summary>
        /// Total bytes received
        /// </summary>
        public long BytesReceived { get; set; }

        /// <summary>
        /// Last message timestamp
        /// </summary>
        public DateTime? LastMessageTime { get; set; }

        /// <summary>
        /// Average message processing latency in milliseconds
        /// </summary>
        public double AverageLatencyMs { get; set; }

        /// <summary>
        /// Total errors encountered
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// Connection uptime in seconds
        /// </summary>
        public double UptimeSeconds { get; set; }

        /// <summary>
        /// Messages per second (calculated)
        /// </summary>
        public double MessagesPerSecond => UptimeSeconds > 0 ? MessageCount / UptimeSeconds : 0;
    }

    /// <summary>
    /// Connection health status
    /// </summary>
    public class ConnectionHealth
    {
        /// <summary>
        /// Exchange name
        /// </summary>
        public string ExchangeName { get; set; }

        /// <summary>
        /// Whether the connection is active
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Whether the connection is authenticated
        /// </summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// Number of reconnection attempts
        /// </summary>
        public int ReconnectAttempts { get; set; }

        /// <summary>
        /// Total successful reconnections
        /// </summary>
        public int TotalReconnects { get; set; }

        /// <summary>
        /// Total message processing failures
        /// </summary>
        public int TotalMessageFailures { get; set; }

        /// <summary>
        /// Connection established time
        /// </summary>
        public DateTime? ConnectedSince { get; set; }

        /// <summary>
        /// Last error message
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Last error timestamp
        /// </summary>
        public DateTime? LastErrorTime { get; set; }

        /// <summary>
        /// Active subscription count
        /// </summary>
        public int ActiveSubscriptions { get; set; }

        /// <summary>
        /// Health status (Healthy, Degraded, Unhealthy)
        /// </summary>
        public HealthStatus Status
        {
            get
            {
                if (!IsConnected) return HealthStatus.Unhealthy;
                if (TotalMessageFailures > 10 || ReconnectAttempts > 3) return HealthStatus.Degraded;
                return HealthStatus.Healthy;
            }
        }
    }

    /// <summary>
    /// Health status enumeration
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>
        /// Connection is healthy with no issues
        /// </summary>
        Healthy,

        /// <summary>
        /// Connection is working but has some issues
        /// </summary>
        Degraded,

        /// <summary>
        /// Connection is not working properly
        /// </summary>
        Unhealthy
    }

    /// <summary>
    /// Observer interface for channel metrics and health monitoring
    /// </summary>
    public interface IChannelObserver
    {
        /// <summary>
        /// Called when a message is received on a channel
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <param name="channel">Channel name</param>
        /// <param name="symbol">Symbol</param>
        /// <param name="messageSize">Message size in bytes</param>
        /// <param name="processingTimeMs">Processing time in milliseconds</param>
        void OnMessageReceived(string exchangeName, string channel, string symbol, int messageSize, double processingTimeMs);

        /// <summary>
        /// Called when a connection state changes
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <param name="isConnected">New connection state</param>
        void OnConnectionStateChanged(string exchangeName, bool isConnected);

        /// <summary>
        /// Called when an error occurs
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <param name="errorMessage">Error message</param>
        void OnError(string exchangeName, string errorMessage);

        /// <summary>
        /// Called when a subscription is added or removed
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <param name="channel">Channel name</param>
        /// <param name="symbol">Symbol</param>
        /// <param name="isActive">Whether subscription is active</param>
        void OnSubscriptionChanged(string exchangeName, string channel, string symbol, bool isActive);

        /// <summary>
        /// Get current statistics for a specific channel
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <param name="channel">Channel name (optional, null for all)</param>
        /// <param name="symbol">Symbol (optional, null for all)</param>
        /// <returns>Channel statistics</returns>
        ChannelStatistics GetStatistics(string exchangeName, string channel = null, string symbol = null);

        /// <summary>
        /// Get connection health status
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        /// <returns>Connection health information</returns>
        ConnectionHealth GetHealth(string exchangeName);

        /// <summary>
        /// Reset statistics for an exchange
        /// </summary>
        /// <param name="exchangeName">Exchange name</param>
        void ResetStatistics(string exchangeName);
    }
}
