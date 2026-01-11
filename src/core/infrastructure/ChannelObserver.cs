using System;
using System.Collections.Concurrent;
using System.Linq;
using CCXT.Collector.Core.Abstractions;

namespace CCXT.Collector.Core.Infrastructure
{
    /// <summary>
    /// Internal statistics tracking class
    /// </summary>
    internal class ExchangeMetrics
    {
        public string ExchangeName { get; set; }
        public DateTime? ConnectedSince { get; set; }
        public bool IsConnected { get; set; }
        public bool IsAuthenticated { get; set; }
        public int ReconnectAttempts { get; set; }
        public int TotalReconnects { get; set; }
        public string LastError { get; set; }
        public DateTime? LastErrorTime { get; set; }

        // Per-channel metrics: key = "channel:symbol"
        public ConcurrentDictionary<string, ChannelMetrics> Channels { get; } = new ConcurrentDictionary<string, ChannelMetrics>();
    }

    /// <summary>
    /// Per-channel metrics
    /// </summary>
    internal class ChannelMetrics
    {
        public string Channel { get; set; }
        public string Symbol { get; set; }
        public long MessageCount { get; set; }
        public long BytesReceived { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public double TotalLatencyMs { get; set; }
        public int ErrorCount { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Default implementation of IChannelObserver for metrics collection
    /// </summary>
    public class ChannelObserver : IChannelObserver
    {
        private readonly ConcurrentDictionary<string, ExchangeMetrics> _exchanges = new ConcurrentDictionary<string, ExchangeMetrics>();

        /// <summary>
        /// Event raised when metrics are updated (for external monitoring systems)
        /// </summary>
        public event Action<string, ChannelStatistics> OnMetricsUpdated;

        /// <summary>
        /// Event raised when health status changes
        /// </summary>
        public event Action<string, ConnectionHealth> OnHealthChanged;

        /// <inheritdoc/>
        public void OnMessageReceived(string exchangeName, string channel, string symbol, int messageSize, double processingTimeMs)
        {
            var metrics = GetOrCreateExchangeMetrics(exchangeName);
            var key = CreateChannelKey(channel, symbol);

            var channelMetrics = metrics.Channels.GetOrAdd(key, _ => new ChannelMetrics
            {
                Channel = channel,
                Symbol = symbol,
                IsActive = true
            });

            channelMetrics.MessageCount++;
            channelMetrics.BytesReceived += messageSize;
            channelMetrics.LastMessageTime = DateTime.UtcNow;
            channelMetrics.TotalLatencyMs += processingTimeMs;

            // Notify listeners
            var stats = CreateStatistics(exchangeName, channelMetrics, metrics.ConnectedSince);
            OnMetricsUpdated?.Invoke(exchangeName, stats);
        }

        /// <inheritdoc/>
        public void OnConnectionStateChanged(string exchangeName, bool isConnected)
        {
            var metrics = GetOrCreateExchangeMetrics(exchangeName);
            var wasConnected = metrics.IsConnected;
            metrics.IsConnected = isConnected;

            if (isConnected && !wasConnected)
            {
                metrics.ConnectedSince = DateTime.UtcNow;
                if (metrics.ReconnectAttempts > 0)
                {
                    metrics.TotalReconnects++;
                }
                metrics.ReconnectAttempts = 0;
            }
            else if (!isConnected && wasConnected)
            {
                metrics.ReconnectAttempts++;
            }

            // Notify health change
            var health = GetHealth(exchangeName);
            OnHealthChanged?.Invoke(exchangeName, health);
        }

        /// <inheritdoc/>
        public void OnError(string exchangeName, string errorMessage)
        {
            var metrics = GetOrCreateExchangeMetrics(exchangeName);
            metrics.LastError = errorMessage;
            metrics.LastErrorTime = DateTime.UtcNow;

            // Increment error count for all active channels
            foreach (var channel in metrics.Channels.Values.Where(c => c.IsActive))
            {
                channel.ErrorCount++;
            }
        }

        /// <inheritdoc/>
        public void OnSubscriptionChanged(string exchangeName, string channel, string symbol, bool isActive)
        {
            var metrics = GetOrCreateExchangeMetrics(exchangeName);
            var key = CreateChannelKey(channel, symbol);

            if (isActive)
            {
                metrics.Channels.GetOrAdd(key, _ => new ChannelMetrics
                {
                    Channel = channel,
                    Symbol = symbol,
                    IsActive = true
                }).IsActive = true;
            }
            else
            {
                if (metrics.Channels.TryGetValue(key, out var channelMetrics))
                {
                    channelMetrics.IsActive = false;
                }
            }
        }

        /// <inheritdoc/>
        public ChannelStatistics GetStatistics(string exchangeName, string channel = null, string symbol = null)
        {
            if (!_exchanges.TryGetValue(exchangeName, out var metrics))
            {
                return new ChannelStatistics { ExchangeName = exchangeName };
            }

            // If specific channel/symbol requested
            if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(symbol))
            {
                var key = CreateChannelKey(channel, symbol);
                if (metrics.Channels.TryGetValue(key, out var channelMetrics))
                {
                    return CreateStatistics(exchangeName, channelMetrics, metrics.ConnectedSince);
                }
                return new ChannelStatistics { ExchangeName = exchangeName, Channel = channel, Symbol = symbol };
            }

            // Aggregate statistics for the exchange
            var aggregated = new ChannelStatistics
            {
                ExchangeName = exchangeName,
                Channel = channel ?? "all",
                Symbol = symbol ?? "all"
            };

            var relevantChannels = metrics.Channels.Values
                .Where(c => (string.IsNullOrEmpty(channel) || c.Channel == channel) &&
                           (string.IsNullOrEmpty(symbol) || c.Symbol == symbol))
                .ToList();

            if (relevantChannels.Count == 0)
                return aggregated;

            aggregated.MessageCount = relevantChannels.Sum(c => c.MessageCount);
            aggregated.BytesReceived = relevantChannels.Sum(c => c.BytesReceived);
            aggregated.ErrorCount = relevantChannels.Sum(c => c.ErrorCount);
            aggregated.LastMessageTime = relevantChannels.Max(c => c.LastMessageTime);

            var totalLatency = relevantChannels.Sum(c => c.TotalLatencyMs);
            var totalMessages = relevantChannels.Sum(c => c.MessageCount);
            aggregated.AverageLatencyMs = totalMessages > 0 ? totalLatency / totalMessages : 0;

            if (metrics.ConnectedSince.HasValue)
            {
                aggregated.UptimeSeconds = (DateTime.UtcNow - metrics.ConnectedSince.Value).TotalSeconds;
            }

            return aggregated;
        }

        /// <inheritdoc/>
        public ConnectionHealth GetHealth(string exchangeName)
        {
            var health = new ConnectionHealth { ExchangeName = exchangeName };

            if (!_exchanges.TryGetValue(exchangeName, out var metrics))
            {
                health.IsConnected = false;
                return health;
            }

            health.IsConnected = metrics.IsConnected;
            health.IsAuthenticated = metrics.IsAuthenticated;
            health.ReconnectAttempts = metrics.ReconnectAttempts;
            health.TotalReconnects = metrics.TotalReconnects;
            health.TotalMessageFailures = metrics.Channels.Values.Sum(c => c.ErrorCount);
            health.ConnectedSince = metrics.ConnectedSince;
            health.LastError = metrics.LastError;
            health.LastErrorTime = metrics.LastErrorTime;
            health.ActiveSubscriptions = metrics.Channels.Values.Count(c => c.IsActive);

            return health;
        }

        /// <inheritdoc/>
        public void ResetStatistics(string exchangeName)
        {
            if (_exchanges.TryGetValue(exchangeName, out var metrics))
            {
                foreach (var channel in metrics.Channels.Values)
                {
                    channel.MessageCount = 0;
                    channel.BytesReceived = 0;
                    channel.TotalLatencyMs = 0;
                    channel.ErrorCount = 0;
                }
                metrics.TotalReconnects = 0;
                metrics.ReconnectAttempts = 0;
                metrics.LastError = null;
                metrics.LastErrorTime = null;
            }
        }

        private ExchangeMetrics GetOrCreateExchangeMetrics(string exchangeName)
        {
            return _exchanges.GetOrAdd(exchangeName, name => new ExchangeMetrics
            {
                ExchangeName = name
            });
        }

        private static string CreateChannelKey(string channel, string symbol)
        {
            return $"{channel}:{symbol}";
        }

        private ChannelStatistics CreateStatistics(string exchangeName, ChannelMetrics channelMetrics, DateTime? connectedSince)
        {
            var stats = new ChannelStatistics
            {
                ExchangeName = exchangeName,
                Channel = channelMetrics.Channel,
                Symbol = channelMetrics.Symbol,
                MessageCount = channelMetrics.MessageCount,
                BytesReceived = channelMetrics.BytesReceived,
                LastMessageTime = channelMetrics.LastMessageTime,
                ErrorCount = channelMetrics.ErrorCount,
                AverageLatencyMs = channelMetrics.MessageCount > 0
                    ? channelMetrics.TotalLatencyMs / channelMetrics.MessageCount
                    : 0
            };

            if (connectedSince.HasValue)
            {
                stats.UptimeSeconds = (DateTime.UtcNow - connectedSince.Value).TotalSeconds;
            }

            return stats;
        }
    }
}
