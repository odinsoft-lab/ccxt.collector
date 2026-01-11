using CCXT.Collector.Models.WebSocket;
using System;
using Xunit;

namespace CCXT.Collector.Tests.Models
{
    /// <summary>
    /// Unit tests for SubscriptionInfo class
    /// </summary>
    public class SubscriptionInfoTests
    {
        #region Constructor Tests

        [Fact]
        public void DefaultConstructor_InitializesDefaultValues()
        {
            var sub = new SubscriptionInfo();

            Assert.Equal(string.Empty, sub.Channel);
            Assert.Equal(string.Empty, sub.Symbol);
            Assert.Equal(string.Empty, sub.SubscriptionId);
            Assert.True(sub.IsActive);
            Assert.Equal(string.Empty, sub.Extra);
            Assert.True(sub.CreatedAt <= DateTime.UtcNow);
            Assert.True(sub.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
        }

        [Fact]
        public void Constructor_WithChannelAndSymbol_SetsProperties()
        {
            var sub = new SubscriptionInfo("orderbook", "BTC/USDT");

            Assert.Equal("orderbook", sub.Channel);
            Assert.Equal("BTC/USDT", sub.Symbol);
            Assert.True(sub.IsActive);
        }

        #endregion

        #region Property Tests

        [Fact]
        public void Channel_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            sub.Channel = "trades";
            Assert.Equal("trades", sub.Channel);
        }

        [Fact]
        public void Symbol_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            sub.Symbol = "ETH/BTC";
            Assert.Equal("ETH/BTC", sub.Symbol);
        }

        [Fact]
        public void SubscriptionId_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            sub.SubscriptionId = "sub-123";
            Assert.Equal("sub-123", sub.SubscriptionId);
        }

        [Fact]
        public void IsActive_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            Assert.True(sub.IsActive);

            sub.IsActive = false;
            Assert.False(sub.IsActive);
        }

        [Fact]
        public void Extra_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            sub.Extra = "1h";
            Assert.Equal("1h", sub.Extra);
        }

        [Fact]
        public void SubscribedAt_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            var now = DateTime.UtcNow;
            sub.SubscribedAt = now;
            Assert.Equal(now, sub.SubscribedAt);
        }

        [Fact]
        public void LastUpdateAt_CanBeSetAndGet()
        {
            var sub = new SubscriptionInfo();
            Assert.Null(sub.LastUpdateAt);

            var now = DateTime.UtcNow;
            sub.LastUpdateAt = now;
            Assert.Equal(now, sub.LastUpdateAt);
        }

        #endregion
    }
}
