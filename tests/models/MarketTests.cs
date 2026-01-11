#nullable enable

using CCXT.Collector.Service;
using System;
using Xunit;

namespace CCXT.Collector.Tests.Models
{
    /// <summary>
    /// Unit tests for Market struct
    /// </summary>
    public class MarketTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ValidInput_SetsProperties()
        {
            var market = new Market("BTC", "USDT");

            Assert.Equal("BTC", market.Base);
            Assert.Equal("USDT", market.Quote);
        }

        [Fact]
        public void Constructor_NullBase_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Market(null!, "USDT"));
        }

        [Fact]
        public void Constructor_NullQuote_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Market("BTC", null!));
        }

        #endregion

        #region ToString Tests

        [Theory]
        [InlineData("BTC", "USDT", "BTC/USDT")]
        [InlineData("ETH", "BTC", "ETH/BTC")]
        [InlineData("XRP", "KRW", "XRP/KRW")]
        public void ToString_ReturnsCorrectFormat(string baseCurrency, string quoteCurrency, string expected)
        {
            var market = new Market(baseCurrency, quoteCurrency);
            Assert.Equal(expected, market.ToString());
        }

        #endregion

        #region Parse Tests

        [Theory]
        [InlineData("BTC/USDT", "BTC", "USDT")]
        [InlineData("ETH/BTC", "ETH", "BTC")]
        [InlineData("XRP/KRW", "XRP", "KRW")]
        public void Parse_ValidSymbol_ReturnsMarket(string symbol, string expectedBase, string expectedQuote)
        {
            var market = Market.Parse(symbol);

            Assert.Equal(expectedBase, market.Base);
            Assert.Equal(expectedQuote, market.Quote);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Parse_NullOrEmpty_ThrowsArgumentNullException(string? symbol)
        {
            Assert.Throws<ArgumentNullException>(() => Market.Parse(symbol!));
        }

        [Theory]
        [InlineData("BTCUSDT")]
        [InlineData("BTC-USDT")]
        [InlineData("BTC/USDT/ETH")]
        [InlineData("invalid")]
        public void Parse_InvalidFormat_ThrowsArgumentException(string symbol)
        {
            Assert.Throws<ArgumentException>(() => Market.Parse(symbol));
        }

        #endregion

        #region TryParse Tests

        [Theory]
        [InlineData("BTC/USDT", true, "BTC", "USDT")]
        [InlineData("ETH/BTC", true, "ETH", "BTC")]
        public void TryParse_ValidSymbol_ReturnsTrueAndMarket(string symbol, bool expectedResult, string expectedBase, string expectedQuote)
        {
            var result = Market.TryParse(symbol, out var market);

            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedBase, market.Base);
            Assert.Equal(expectedQuote, market.Quote);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("BTCUSDT")]
        [InlineData("BTC-USDT")]
        [InlineData("invalid")]
        public void TryParse_InvalidInput_ReturnsFalse(string? symbol)
        {
            var result = Market.TryParse(symbol!, out var market);

            Assert.False(result);
            Assert.Equal(default, market);
        }

        #endregion

        #region Equality Tests

        [Fact]
        public void Equals_SameMarkets_ReturnsTrue()
        {
            var market1 = new Market("BTC", "USDT");
            var market2 = new Market("BTC", "USDT");

            Assert.True(market1.Equals(market2));
            Assert.True(market1 == market2);
            Assert.False(market1 != market2);
        }

        [Fact]
        public void Equals_DifferentMarkets_ReturnsFalse()
        {
            var market1 = new Market("BTC", "USDT");
            var market2 = new Market("ETH", "USDT");

            Assert.False(market1.Equals(market2));
            Assert.False(market1 == market2);
            Assert.True(market1 != market2);
        }

        [Fact]
        public void Equals_DifferentQuote_ReturnsFalse()
        {
            var market1 = new Market("BTC", "USDT");
            var market2 = new Market("BTC", "KRW");

            Assert.False(market1.Equals(market2));
        }

        [Fact]
        public void Equals_Object_ReturnsCorrectly()
        {
            var market1 = new Market("BTC", "USDT");
            object market2 = new Market("BTC", "USDT");
            object notMarket = "not a market";

            Assert.True(market1.Equals(market2));
            Assert.False(market1.Equals(notMarket));
            Assert.False(market1.Equals(null));
        }

        [Fact]
        public void GetHashCode_SameMarkets_ReturnsSameHash()
        {
            var market1 = new Market("BTC", "USDT");
            var market2 = new Market("BTC", "USDT");

            Assert.Equal(market1.GetHashCode(), market2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_DifferentMarkets_ReturnsDifferentHash()
        {
            var market1 = new Market("BTC", "USDT");
            var market2 = new Market("ETH", "USDT");

            // Different hash codes are expected but not guaranteed
            // This test verifies they are not equal in most cases
            Assert.NotEqual(market1.GetHashCode(), market2.GetHashCode());
        }

        #endregion
    }
}
