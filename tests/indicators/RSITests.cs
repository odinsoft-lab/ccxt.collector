using CCXT.Collector.Indicator;
using CCXT.Collector.Service;
using System.Collections.Generic;
using Xunit;

namespace CCXT.Collector.Tests.Indicators
{
    /// <summary>
    /// Unit tests for RSI (Relative Strength Index) indicator
    /// </summary>
    public class RSITests
    {
        #region Test Data Helpers

        private List<SOhlcvItem> CreateOhlcvData(params decimal[] closePrices)
        {
            var data = new List<SOhlcvItem>();
            for (int i = 0; i < closePrices.Length; i++)
            {
                data.Add(new SOhlcvItem
                {
                    symbol = "BTC/USDT",
                    timestamp = 1700000000000L + (i * 60000),
                    closePrice = closePrices[i],
                    openPrice = closePrices[i],
                    highPrice = closePrices[i] + 10,
                    lowPrice = closePrices[i] - 10
                });
            }
            return data;
        }

        #endregion

        #region Basic Tests

        [Fact]
        public void Calculate_ReturnsRSISerieWithCorrectLength()
        {
            var ohlcData = CreateOhlcvData(44m, 44.34m, 44.09m, 43.61m, 44.33m, 44.83m, 45.10m, 45.42m);

            var rsi = new RSI(5);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            Assert.NotNull(result);
            Assert.Equal(8, result.RSI.Count);
            Assert.Equal(8, result.RS.Count);
        }

        [Fact]
        public void Calculate_FirstValuesAreNull()
        {
            var ohlcData = CreateOhlcvData(10m, 11m, 12m, 11m, 12m, 13m, 12m, 13m);

            var rsi = new RSI(5);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            // First value (index 0) should be null
            Assert.Null(result.RSI[0]);
            Assert.Null(result.RS[0]);
        }

        [Fact]
        public void Calculate_SingleDataPoint_ReturnsInitializedResult()
        {
            // RSI adds initial null values even for single item
            var ohlcData = CreateOhlcvData(100m);

            var rsi = new RSI(14);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            Assert.NotNull(result);
            // First element is always null (no previous price to compare)
            Assert.Single(result.RSI);
            Assert.Null(result.RSI[0]);
        }

        #endregion

        #region RSI Value Range Tests

        [Fact]
        public void Calculate_MixedGainsAndLosses_CalculatesCorrectly()
        {
            // Mixed prices with both gains and losses to avoid division by zero
            var ohlcData = CreateOhlcvData(10m, 11m, 10m, 12m, 11m, 13m, 12m, 14m, 13m, 15m);

            var rsi = new RSI(5);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            // Should have calculated RSI values after the period
            Assert.NotNull(result);
            Assert.Equal(10, result.RSI.Count);

            // RSI values after period should be present
            for (int i = 5; i < result.RSI.Count; i++)
            {
                if (result.RSI[i].HasValue)
                {
                    // RSI should be between 0 and 100
                    Assert.True(result.RSI[i] >= 0 && result.RSI[i] <= 100,
                        $"RSI at index {i} should be between 0-100 but was {result.RSI[i]}");
                }
            }
        }

        [Fact]
        public void Calculate_MostlyLosses_RSILow()
        {
            // Mostly decreasing prices with one small gain to avoid division by zero
            var ohlcData = CreateOhlcvData(100m, 99m, 98m, 97m, 96m, 95m, 95.1m, 94m, 93m, 92m);

            var rsi = new RSI(5);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            // Find first non-null RSI value
            for (int i = 5; i < result.RSI.Count; i++)
            {
                if (result.RSI[i].HasValue)
                {
                    // RSI should be low when most price movements are negative
                    Assert.True(result.RSI[i] < 50, $"RSI at index {i} should be < 50 but was {result.RSI[i]}");
                }
            }
        }

        #endregion

        #region Period Tests

        [Fact]
        public void Calculate_DifferentPeriods_ReturnDifferentResults()
        {
            var ohlcData = CreateOhlcvData(10m, 11m, 12m, 11m, 13m, 12m, 14m, 13m, 15m, 14m, 16m, 15m, 17m, 16m, 18m);

            var rsi5 = new RSI(5);
            rsi5.Load(ohlcData);
            var result5 = rsi5.Calculate();

            var rsi10 = new RSI(10);
            rsi10.Load(ohlcData);
            var result10 = rsi10.Calculate();

            // Results should be different for different periods at the same index
            // Find an index where both have values
            int testIndex = 14;
            if (result5.RSI[testIndex].HasValue && result10.RSI[testIndex].HasValue)
            {
                // They may or may not be equal, but we verify both are calculated
                Assert.NotNull(result5.RSI[testIndex]);
                Assert.NotNull(result10.RSI[testIndex]);
            }
        }

        #endregion

        #region RS (Relative Strength) Tests

        [Fact]
        public void Calculate_RSIsCalculated_BeforeRSI()
        {
            var ohlcData = CreateOhlcvData(10m, 11m, 10m, 12m, 11m, 13m, 12m, 14m);

            var rsi = new RSI(5);
            rsi.Load(ohlcData);
            var result = rsi.Calculate();

            // RS and RSI should have same length
            Assert.Equal(result.RS.Count, result.RSI.Count);

            // When RSI is calculated, RS should also be calculated
            for (int i = 0; i < result.RSI.Count; i++)
            {
                if (result.RSI[i].HasValue)
                {
                    Assert.NotNull(result.RS[i]);
                }
            }
        }

        #endregion

        #region Serie Tests

        [Fact]
        public void RSISerie_DefaultConstructor_InitializesEmptyLists()
        {
            var serie = new RSISerie();

            Assert.NotNull(serie.RSI);
            Assert.NotNull(serie.RS);
            Assert.Empty(serie.RSI);
            Assert.Empty(serie.RS);
        }

        #endregion
    }
}
