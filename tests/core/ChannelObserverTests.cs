#nullable enable

using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Core.Infrastructure;
using Xunit;

namespace CCXT.Collector.Tests.Core
{
    /// <summary>
    /// Unit tests for ChannelObserver and related observability classes
    /// </summary>
    public class ChannelObserverTests
    {
        #region ChannelStatistics Tests

        [Fact]
        public void ChannelStatistics_DefaultValues_AreCorrect()
        {
            var stats = new ChannelStatistics();

            Assert.Null(stats.ExchangeName);
            Assert.Null(stats.Channel);
            Assert.Null(stats.Symbol);
            Assert.Equal(0, stats.MessageCount);
            Assert.Equal(0, stats.BytesReceived);
            Assert.Null(stats.LastMessageTime);
            Assert.Equal(0, stats.AverageLatencyMs);
            Assert.Equal(0, stats.ErrorCount);
            Assert.Equal(0, stats.UptimeSeconds);
            Assert.Equal(0, stats.MessagesPerSecond);
        }

        [Fact]
        public void ChannelStatistics_MessagesPerSecond_CalculatesCorrectly()
        {
            var stats = new ChannelStatistics
            {
                MessageCount = 1000,
                UptimeSeconds = 100
            };

            Assert.Equal(10, stats.MessagesPerSecond);
        }

        [Fact]
        public void ChannelStatistics_MessagesPerSecond_ZeroUptime_ReturnsZero()
        {
            var stats = new ChannelStatistics
            {
                MessageCount = 1000,
                UptimeSeconds = 0
            };

            Assert.Equal(0, stats.MessagesPerSecond);
        }

        #endregion

        #region ConnectionHealth Tests

        [Fact]
        public void ConnectionHealth_Status_Healthy_WhenConnected()
        {
            var health = new ConnectionHealth
            {
                IsConnected = true,
                TotalMessageFailures = 0,
                ReconnectAttempts = 0
            };

            Assert.Equal(HealthStatus.Healthy, health.Status);
        }

        [Fact]
        public void ConnectionHealth_Status_Unhealthy_WhenDisconnected()
        {
            var health = new ConnectionHealth
            {
                IsConnected = false
            };

            Assert.Equal(HealthStatus.Unhealthy, health.Status);
        }

        [Fact]
        public void ConnectionHealth_Status_Degraded_WhenHighFailures()
        {
            var health = new ConnectionHealth
            {
                IsConnected = true,
                TotalMessageFailures = 15
            };

            Assert.Equal(HealthStatus.Degraded, health.Status);
        }

        [Fact]
        public void ConnectionHealth_Status_Degraded_WhenHighReconnects()
        {
            var health = new ConnectionHealth
            {
                IsConnected = true,
                ReconnectAttempts = 5
            };

            Assert.Equal(HealthStatus.Degraded, health.Status);
        }

        #endregion

        #region ChannelObserver Tests

        [Fact]
        public void OnMessageReceived_TracksStatistics()
        {
            var observer = new ChannelObserver();

            observer.OnMessageReceived("Binance", "ticker", "BTC/USDT", 100, 5.0);
            observer.OnMessageReceived("Binance", "ticker", "BTC/USDT", 150, 3.0);

            var stats = observer.GetStatistics("Binance", "ticker", "BTC/USDT");

            Assert.Equal("Binance", stats.ExchangeName);
            Assert.Equal("ticker", stats.Channel);
            Assert.Equal("BTC/USDT", stats.Symbol);
            Assert.Equal(2, stats.MessageCount);
            Assert.Equal(250, stats.BytesReceived);
            Assert.Equal(4.0, stats.AverageLatencyMs); // (5 + 3) / 2
        }

        [Fact]
        public void OnConnectionStateChanged_TracksConnectionStatus()
        {
            var observer = new ChannelObserver();

            observer.OnConnectionStateChanged("Binance", true);
            var health = observer.GetHealth("Binance");

            Assert.True(health.IsConnected);
            Assert.NotNull(health.ConnectedSince);
        }

        [Fact]
        public void OnConnectionStateChanged_TracksReconnects()
        {
            var observer = new ChannelObserver();

            // Initial connection
            observer.OnConnectionStateChanged("Binance", true);
            // Disconnect
            observer.OnConnectionStateChanged("Binance", false);
            // Reconnect
            observer.OnConnectionStateChanged("Binance", true);

            var health = observer.GetHealth("Binance");

            Assert.True(health.IsConnected);
            Assert.Equal(1, health.TotalReconnects);
        }

        [Fact]
        public void OnError_TracksErrorInfo()
        {
            var observer = new ChannelObserver();

            observer.OnConnectionStateChanged("Binance", true);
            observer.OnSubscriptionChanged("Binance", "ticker", "BTC/USDT", true);
            observer.OnError("Binance", "Connection timeout");

            var health = observer.GetHealth("Binance");

            Assert.Equal("Connection timeout", health.LastError);
            Assert.NotNull(health.LastErrorTime);
        }

        [Fact]
        public void OnSubscriptionChanged_TracksActiveSubscriptions()
        {
            var observer = new ChannelObserver();

            observer.OnConnectionStateChanged("Binance", true);
            observer.OnSubscriptionChanged("Binance", "ticker", "BTC/USDT", true);
            observer.OnSubscriptionChanged("Binance", "orderbook", "ETH/USDT", true);

            var health = observer.GetHealth("Binance");

            Assert.Equal(2, health.ActiveSubscriptions);
        }

        [Fact]
        public void GetStatistics_AggregatesAllChannels()
        {
            var observer = new ChannelObserver();

            observer.OnMessageReceived("Binance", "ticker", "BTC/USDT", 100, 5.0);
            observer.OnMessageReceived("Binance", "orderbook", "BTC/USDT", 200, 10.0);

            var stats = observer.GetStatistics("Binance");

            Assert.Equal(2, stats.MessageCount);
            Assert.Equal(300, stats.BytesReceived);
        }

        [Fact]
        public void GetHealth_UnknownExchange_ReturnsDisconnected()
        {
            var observer = new ChannelObserver();

            var health = observer.GetHealth("Unknown");

            Assert.False(health.IsConnected);
            Assert.Equal("Unknown", health.ExchangeName);
        }

        [Fact]
        public void ResetStatistics_ClearsCounters()
        {
            var observer = new ChannelObserver();

            observer.OnConnectionStateChanged("Binance", true);
            observer.OnMessageReceived("Binance", "ticker", "BTC/USDT", 100, 5.0);
            observer.OnError("Binance", "Test error");

            observer.ResetStatistics("Binance");

            var stats = observer.GetStatistics("Binance", "ticker", "BTC/USDT");
            var health = observer.GetHealth("Binance");

            Assert.Equal(0, stats.MessageCount);
            Assert.Equal(0, stats.ErrorCount);
            Assert.Null(health.LastError);
        }

        [Fact]
        public void OnMetricsUpdated_Event_IsFired()
        {
            var observer = new ChannelObserver();
            ChannelStatistics? receivedStats = null;

            observer.OnMetricsUpdated += (exchange, stats) => receivedStats = stats;

            observer.OnMessageReceived("Binance", "ticker", "BTC/USDT", 100, 5.0);

            Assert.NotNull(receivedStats);
            Assert.Equal("Binance", receivedStats.ExchangeName);
            Assert.Equal("ticker", receivedStats.Channel);
        }

        [Fact]
        public void OnHealthChanged_Event_IsFired()
        {
            var observer = new ChannelObserver();
            ConnectionHealth? receivedHealth = null;

            observer.OnHealthChanged += (exchange, health) => receivedHealth = health;

            observer.OnConnectionStateChanged("Binance", true);

            Assert.NotNull(receivedHealth);
            Assert.Equal("Binance", receivedHealth.ExchangeName);
            Assert.True(receivedHealth.IsConnected);
        }

        #endregion

        #region HealthStatus Tests

        [Fact]
        public void HealthStatus_HasCorrectValues()
        {
            Assert.Equal(0, (int)HealthStatus.Healthy);
            Assert.Equal(1, (int)HealthStatus.Degraded);
            Assert.Equal(2, (int)HealthStatus.Unhealthy);
        }

        #endregion
    }
}
