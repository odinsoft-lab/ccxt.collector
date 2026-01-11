using CCXT.Collector.Indicator;
using CCXT.Collector.Service;
using System.Collections.Generic;
using Xunit;

namespace CCXT.Collector.Tests.Indicators
{
    /// <summary>
    /// Unit tests for BollingerBand indicator
    /// </summary>
    public class BollingerBandTests
    {
        #region Test Data Helpers

        private List<SOhlcvItem> CreateOhlcvData(int count)
        {
            var data = new List<SOhlcvItem>();
            for (int i = 0; i < count; i++)
            {
                var price = 100m + (i % 10); // Oscillating prices
                data.Add(new SOhlcvItem
                {
                    symbol = "BTC/USDT",
                    timestamp = 1700000000000L + (i * 60000),
                    closePrice = price,
                    openPrice = price - 1,
                    highPrice = price + 2,
                    lowPrice = price - 2
                });
            }
            return data;
        }

        private List<SOhlcvItem> CreateOhlcvDataWithPrices(params (decimal high, decimal low, decimal close)[] prices)
        {
            var data = new List<SOhlcvItem>();
            for (int i = 0; i < prices.Length; i++)
            {
                data.Add(new SOhlcvItem
                {
                    symbol = "BTC/USDT",
                    timestamp = 1700000000000L + (i * 60000),
                    highPrice = prices[i].high,
                    lowPrice = prices[i].low,
                    closePrice = prices[i].close,
                    openPrice = prices[i].close
                });
            }
            return data;
        }

        #endregion

        #region Calculation Tests

        [Fact]
        public void Calculate_ReturnsCorrectLength()
        {
            var ohlcData = CreateOhlcvData(30);

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            Assert.NotNull(result);
            Assert.Equal(30, result.MidBand.Count);
            Assert.Equal(30, result.UpperBand.Count);
            Assert.Equal(30, result.LowerBand.Count);
            Assert.Equal(30, result.BandWidth.Count);
            Assert.Equal(30, result.BPercent.Count);
        }

        [Fact]
        public void Calculate_FirstValuesAreNull()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            // First period-1 values should be null
            for (int i = 0; i < 19; i++)
            {
                Assert.Null(result.MidBand[i]);
                Assert.Null(result.UpperBand[i]);
                Assert.Null(result.LowerBand[i]);
                Assert.Null(result.BandWidth[i]);
                Assert.Null(result.BPercent[i]);
            }

            // From index period-1, values should be present
            Assert.NotNull(result.MidBand[19]);
            Assert.NotNull(result.UpperBand[19]);
            Assert.NotNull(result.LowerBand[19]);
        }

        [Fact]
        public void Calculate_UpperBandGreaterThanLowerBand()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            // Upper band should always be >= lower band
            for (int i = 19; i < result.UpperBand.Count; i++)
            {
                Assert.True(result.UpperBand[i] >= result.LowerBand[i],
                    $"Upper band ({result.UpperBand[i]}) should be >= Lower band ({result.LowerBand[i]}) at index {i}");
            }
        }

        [Fact]
        public void Calculate_MidBandBetweenUpperAndLower()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            // Mid band should be between upper and lower
            for (int i = 19; i < result.MidBand.Count; i++)
            {
                Assert.True(result.MidBand[i] >= result.LowerBand[i],
                    $"Mid band should be >= Lower band at index {i}");
                Assert.True(result.MidBand[i] <= result.UpperBand[i],
                    $"Mid band should be <= Upper band at index {i}");
            }
        }

        [Fact]
        public void Calculate_EmptyData_ReturnsEmptyResult()
        {
            var ohlcData = new List<SOhlcvItem>();

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            Assert.NotNull(result);
            Assert.Empty(result.MidBand);
            Assert.Empty(result.UpperBand);
            Assert.Empty(result.LowerBand);
        }

        #endregion

        #region BandWidth Tests

        [Fact]
        public void Calculate_BandWidthIsPositive()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb = new BollingerBand(20, 2);
            bb.Load(ohlcData);
            var result = bb.Calculate();

            for (int i = 19; i < result.BandWidth.Count; i++)
            {
                Assert.True(result.BandWidth[i] >= 0,
                    $"BandWidth should be >= 0 at index {i}, but was {result.BandWidth[i]}");
            }
        }

        #endregion

        #region Period and Factor Tests

        [Fact]
        public void Calculate_DifferentPeriods_ReturnDifferentResults()
        {
            var ohlcData = CreateOhlcvData(30);

            var bb10 = new BollingerBand(10, 2);
            bb10.Load(ohlcData);
            var result10 = bb10.Calculate();

            var bb20 = new BollingerBand(20, 2);
            bb20.Load(ohlcData);
            var result20 = bb20.Calculate();

            // With smaller period, we should have more non-null values
            int nonNull10 = 0;
            int nonNull20 = 0;

            for (int i = 0; i < 30; i++)
            {
                if (result10.MidBand[i].HasValue) nonNull10++;
                if (result20.MidBand[i].HasValue) nonNull20++;
            }

            Assert.True(nonNull10 > nonNull20);
        }

        [Fact]
        public void Calculate_LargerFactor_WiderBands()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb1 = new BollingerBand(20, 1);
            bb1.Load(ohlcData);
            var result1 = bb1.Calculate();

            // Need to create new data since Calculate modifies close prices
            var ohlcData2 = CreateOhlcvData(25);
            var bb3 = new BollingerBand(20, 3);
            bb3.Load(ohlcData2);
            var result3 = bb3.Calculate();

            // Larger factor should produce wider bands (larger bandwidth)
            int index = 24;
            if (result1.BandWidth[index].HasValue && result3.BandWidth[index].HasValue)
            {
                Assert.True(result3.BandWidth[index] > result1.BandWidth[index],
                    "Larger factor should produce wider bands");
            }
        }

        #endregion

        #region Default Constructor Tests

        [Fact]
        public void DefaultConstructor_UsesPeriod20Factor2()
        {
            var ohlcData = CreateOhlcvData(25);

            var bb = new BollingerBand();
            bb.Load(ohlcData);
            var result = bb.Calculate();

            // First 19 values should be null (period 20)
            for (int i = 0; i < 19; i++)
            {
                Assert.Null(result.MidBand[i]);
            }

            // 20th value (index 19) should be the first band
            Assert.NotNull(result.MidBand[19]);
        }

        #endregion

        #region BollingerBandSerie Tests

        [Fact]
        public void BollingerBandSerie_DefaultConstructor_InitializesEmptyLists()
        {
            var serie = new BollingerBandSerie();

            Assert.NotNull(serie.MidBand);
            Assert.NotNull(serie.UpperBand);
            Assert.NotNull(serie.LowerBand);
            Assert.NotNull(serie.BandWidth);
            Assert.NotNull(serie.BPercent);

            Assert.Empty(serie.MidBand);
            Assert.Empty(serie.UpperBand);
            Assert.Empty(serie.LowerBand);
            Assert.Empty(serie.BandWidth);
            Assert.Empty(serie.BPercent);
        }

        #endregion
    }
}
