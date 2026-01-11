#nullable enable

using CCXT.Collector.Core.Infrastructure;
using CCXT.Collector.Service;
using Xunit;

namespace CCXT.Collector.Tests.Utilities
{
    /// <summary>
    /// Unit tests for ParsingHelpers utility class
    /// </summary>
    public class ParsingHelpersTests
    {
        #region NormalizeSymbol Tests

        [Theory]
        [InlineData("btc/usdt", "BTC/USDT")]
        [InlineData("BTC/USDT", "BTC/USDT")]
        [InlineData("btc-usdt", "BTC/USDT")]
        [InlineData("BTC-USDT", "BTC/USDT")]
        [InlineData("BTCUSDT", "BTC/USDT")]
        [InlineData("btcusdt", "BTC/USDT")]
        [InlineData("ethbtc", "ETH/BTC")]
        [InlineData("ETHBTC", "ETH/BTC")]
        [InlineData("xrpkrw", "XRP/KRW")]
        [InlineData("BNB/BUSD", "BNB/BUSD")]
        public void NormalizeSymbol_VariousFormats_ReturnsStandardFormat(string input, string expected)
        {
            var result = ParsingHelpers.NormalizeSymbol(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeSymbol_NullOrEmpty_ReturnsInput(string? input)
        {
            var result = ParsingHelpers.NormalizeSymbol(input!);
            Assert.Equal(input, result);
        }

        [Fact]
        public void NormalizeSymbol_UnknownQuote_ReturnsUppercase()
        {
            // Unknown quote currency, can't split
            var result = ParsingHelpers.NormalizeSymbol("BTCXYZ");
            Assert.Equal("BTCXYZ", result);
        }

        #endregion

        #region RemoveDelimiter Tests

        [Theory]
        [InlineData("BTC/USDT", "BTCUSDT")]
        [InlineData("btc-usdt", "BTCUSDT")]
        [InlineData("ETH/BTC", "ETHBTC")]
        public void RemoveDelimiter_ReturnsNoSlashSymbol(string input, string expected)
        {
            var result = ParsingHelpers.RemoveDelimiter(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Upbit Format Tests

        [Theory]
        [InlineData("BTC/KRW", "KRW-BTC")]
        [InlineData("ETH/KRW", "KRW-ETH")]
        [InlineData("XRP/USDT", "USDT-XRP")]
        public void ToUpbitCode_StandardToUpbit_ReturnsCorrectFormat(string input, string expected)
        {
            var result = ParsingHelpers.ToUpbitCode(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("KRW-BTC", "BTC/KRW")]
        [InlineData("KRW-ETH", "ETH/KRW")]
        [InlineData("USDT-XRP", "XRP/USDT")]
        public void FromUpbitCode_UpbitToStandard_ReturnsCorrectFormat(string input, string expected)
        {
            var result = ParsingHelpers.FromUpbitCode(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FromUpbitCode_NullOrEmpty_ReturnsInput(string? input)
        {
            var result = ParsingHelpers.FromUpbitCode(input!);
            Assert.Equal(input, result);
        }

        #endregion

        #region Interval Normalization Tests

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("5m", "5m")]
        [InlineData("60m", "1h")]
        [InlineData("24h", "1d")]
        [InlineData("7d", "1w")]
        [InlineData("30d", "1M")]
        [InlineData("1H", "1h")]
        [InlineData("1D", "1d")]
        public void NormalizeInterval_VariousFormats_ReturnsStandard(string input, string expected)
        {
            var result = ParsingHelpers.NormalizeInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NormalizeInterval_NullOrEmpty_ReturnsInput(string? input)
        {
            var result = ParsingHelpers.NormalizeInterval(input!);
            Assert.Equal(input, result);
        }

        #endregion

        #region Upbit Interval Tests

        [Theory]
        [InlineData("1m", "1")]
        [InlineData("5m", "5")]
        [InlineData("15m", "15")]
        [InlineData("1h", "60")]
        [InlineData("4h", "240")]
        [InlineData("1d", "D")]
        [InlineData("1w", "W")]
        [InlineData("30d", "M")] // Note: NormalizeInterval converts 30d to 1M which then maps to M
        public void ToUpbitIntervalUnit_StandardToUpbit_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToUpbitIntervalUnit(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1", "1m")]
        [InlineData("60", "1h")]
        [InlineData("240", "4h")]
        [InlineData("D", "1d")]
        [InlineData("W", "1w")]
        [InlineData("M", "1M")]
        public void FromUpbitIntervalUnit_UpbitToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromUpbitIntervalUnit(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Dash Symbol Tests

        [Theory]
        [InlineData("BTC/USDT", "BTC-USDT")]
        [InlineData("ETH/BTC", "ETH-BTC")]
        public void ToDashSymbol_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToDashSymbol(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("BTC-USDT", "BTC/USDT")]
        [InlineData("ETH-BTC", "ETH/BTC")]
        public void FromDashSymbol_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromDashSymbol(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FromDashSymbol_Null_ReturnsNull()
        {
            var result = ParsingHelpers.FromDashSymbol(null!);
            Assert.Null(result);
        }

        #endregion

        #region Bitget Interval Tests

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("5m", "5m")]
        [InlineData("1h", "1H")]
        [InlineData("4h", "4H")]
        [InlineData("1d", "1D")]
        [InlineData("1w", "1W")]
        public void ToBitgetChannelInterval_StandardToBitget_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToBitgetChannelInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("candle1m", "1m")]
        [InlineData("candle1H", "1h")]
        [InlineData("candle4H", "4h")]
        [InlineData("candle1D", "1d")]
        public void FromBitgetChannelInterval_BitgetToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromBitgetChannelInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void FromBitgetChannelInterval_NullOrEmpty_ReturnsDefault(string? input)
        {
            var result = ParsingHelpers.FromBitgetChannelInterval(input!);
            Assert.Equal("1m", result);
        }

        #endregion

        #region IntervalToMilliseconds Tests

        [Theory]
        [InlineData("1m", 60_000L)]
        [InlineData("5m", 300_000L)]
        [InlineData("15m", 900_000L)]
        [InlineData("1h", 3_600_000L)]
        [InlineData("4h", 14_400_000L)]
        [InlineData("1d", 86_400_000L)]
        [InlineData("1w", 604_800_000L)]
        [InlineData("30d", 2_592_000_000L)] // Note: NormalizeInterval converts 30d to 1M
        public void IntervalToMilliseconds_ReturnsCorrectMs(string input, long expected)
        {
            var result = ParsingHelpers.IntervalToMilliseconds(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IntervalToMilliseconds_UnknownInterval_ReturnsDefault()
        {
            var result = ParsingHelpers.IntervalToMilliseconds("unknown");
            Assert.Equal(3_600_000L, result); // defaults to 1h
        }

        #endregion

        #region Order Type/Status Parsing Tests

        [Theory]
        [InlineData("LIMIT", OrderType.Limit)]
        [InlineData("MARKET", OrderType.Market)]
        [InlineData("STOP", OrderType.Stop)]
        [InlineData("STOP_LOSS", OrderType.Stop)]
        [InlineData("STOP_LIMIT", OrderType.StopLimit)]
        [InlineData("TAKE_PROFIT", OrderType.TakeProfit)]
        [InlineData("TAKE_PROFIT_LIMIT", OrderType.TakeProfitLimit)]
        [InlineData("limit", OrderType.Limit)]
        [InlineData("market", OrderType.Market)]
        public void ParseGenericOrderType_ReturnsCorrectType(string input, OrderType expected)
        {
            var result = ParsingHelpers.ParseGenericOrderType(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("unknown")]
        public void ParseGenericOrderType_InvalidInput_ReturnsLimit(string? input)
        {
            var result = ParsingHelpers.ParseGenericOrderType(input!);
            Assert.Equal(OrderType.Limit, result);
        }

        [Theory]
        [InlineData("NEW", OrderStatus.New)]
        [InlineData("OPEN", OrderStatus.Open)]
        [InlineData("WAIT", OrderStatus.Open)]
        [InlineData("PARTIALLY_FILLED", OrderStatus.PartiallyFilled)]
        [InlineData("PARTIAL", OrderStatus.PartiallyFilled)]
        [InlineData("FILLED", OrderStatus.Filled)]
        [InlineData("DONE", OrderStatus.Filled)]
        [InlineData("CANCELED", OrderStatus.Canceled)]
        [InlineData("CANCELLED", OrderStatus.Canceled)]
        [InlineData("REJECTED", OrderStatus.Rejected)]
        [InlineData("EXPIRED", OrderStatus.Expired)]
        [InlineData("LIVE", OrderStatus.Open)]
        [InlineData("SUBMITTED", OrderStatus.Open)]
        public void ParseGenericOrderStatus_ReturnsCorrectStatus(string input, OrderStatus expected)
        {
            var result = ParsingHelpers.ParseGenericOrderStatus(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseGenericOrderStatus_NullOrEmpty_ReturnsNew(string? input)
        {
            var result = ParsingHelpers.ParseGenericOrderStatus(input!);
            Assert.Equal(OrderStatus.New, result);
        }

        #endregion

        #region OKX Interval Tests

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("1h", "1H")]
        [InlineData("4h", "4H")]
        [InlineData("1d", "1D")]
        [InlineData("1w", "1W")]
        // Note: "1M" gets normalized to "1m" due to ToLowerInvariant()
        public void ToOkxInterval_StandardToOkx_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToOkxInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("candle1m", "1m")]
        [InlineData("candle1H", "1h")]
        [InlineData("candle4H", "4h")]
        [InlineData("candle1D", "1d")]
        public void FromOkxInterval_OkxToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromOkxInterval(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Bybit Interval Tests

        [Theory]
        [InlineData("1m", "1")]
        [InlineData("5m", "5")]
        [InlineData("1h", "60")]
        [InlineData("4h", "240")]
        [InlineData("1d", "D")]
        [InlineData("1w", "W")]
        // Note: "1M" gets normalized to "1m" due to ToLowerInvariant()
        public void ToBybitIntervalCode_StandardToBybit_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToBybitIntervalCode(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1", "1m")]
        [InlineData("60", "1h")]
        [InlineData("240", "4h")]
        [InlineData("D", "1d")]
        [InlineData("W", "1w")]
        [InlineData("M", "1M")]
        public void FromBybitIntervalCode_BybitToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromBybitIntervalCode(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Huobi Interval Tests

        [Theory]
        [InlineData("1m", "1min")]
        [InlineData("5m", "5min")]
        [InlineData("15m", "15min")]
        [InlineData("1h", "60min")]
        [InlineData("4h", "4hour")]
        [InlineData("1d", "1day")]
        [InlineData("1w", "1week")]
        // Note: "1M" gets normalized to "1m" due to ToLowerInvariant()
        public void ToHuobiInterval_StandardToHuobi_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToHuobiInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1min", "1m")]
        [InlineData("60min", "1h")]
        [InlineData("4hour", "4h")]
        [InlineData("1day", "1d")]
        [InlineData("1week", "1w")]
        [InlineData("1mon", "1M")]
        public void FromHuobiInterval_HuobiToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromHuobiInterval(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Bittrex Interval Tests

        [Theory]
        [InlineData("1m", "MINUTE_1")]
        [InlineData("5m", "MINUTE_5")]
        [InlineData("1h", "HOUR_1")]
        [InlineData("4h", "HOUR_4")]
        [InlineData("1d", "DAY_1")]
        public void ToBittrexInterval_StandardToBittrex_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToBittrexInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("MINUTE_1", "1m")]
        [InlineData("MINUTE_5", "5m")]
        [InlineData("HOUR_1", "1h")]
        [InlineData("DAY_1", "1d")]
        public void FromBittrexInterval_BittrexToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromBittrexInterval(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Gate.io Interval Tests

        [Theory]
        [InlineData("1m", "1m")]
        [InlineData("60m", "1h")]
        [InlineData("1h", "1h")]
        public void ToGateioInterval_ReturnsNormalized(string input, string expected)
        {
            var result = ParsingHelpers.ToGateioInterval(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Crypto.com Interval Tests

        [Theory]
        [InlineData("1m", "1M")]
        [InlineData("5m", "5M")]
        [InlineData("1h", "1H")]
        [InlineData("4h", "4H")]
        [InlineData("1d", "1D")]
        [InlineData("1w", "7D")]
        public void ToCryptoInterval_StandardToCrypto_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToCryptoInterval(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1M", "1m")]
        [InlineData("5M", "5m")]
        [InlineData("1H", "1h")]
        [InlineData("1D", "1d")]
        [InlineData("7D", "1w")]
        public void FromCryptoInterval_CryptoToStandard_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromCryptoInterval(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Underscore Symbol Tests

        [Theory]
        [InlineData("BTC/USDT", "BTC_USDT")]
        [InlineData("ETH/BTC", "ETH_BTC")]
        public void ToUnderscoreSymbol_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.ToUnderscoreSymbol(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("BTC_USDT", "BTC/USDT")]
        [InlineData("ETH_BTC", "ETH/BTC")]
        public void FromUnderscoreSymbol_ReturnsCorrect(string input, string expected)
        {
            var result = ParsingHelpers.FromUnderscoreSymbol(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FromUnderscoreSymbol_Null_ReturnsNull()
        {
            var result = ParsingHelpers.FromUnderscoreSymbol(null!);
            Assert.Null(result);
        }

        #endregion
    }
}
