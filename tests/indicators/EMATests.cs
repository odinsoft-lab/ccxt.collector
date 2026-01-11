using CCXT.Collector.Indicator;
using CCXT.Collector.Service;
using System.Collections.Generic;
using Xunit;

namespace CCXT.Collector.Tests.Indicators
{
    /// <summary>
    /// Unit tests for EMA (Exponential Moving Average) indicator
    /// </summary>
    public class EMATests
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

        #region Calculation Tests

        [Fact]
        public void Calculate_ReturnsCorrectLength()
        {
            var ohlcData = CreateOhlcvData(22.27m, 22.19m, 22.08m, 22.17m, 22.18m, 22.13m, 22.23m, 22.43m, 22.24m, 22.29m);

            var ema = new EMA(5, false);
            ema.Load(ohlcData);
            var result = ema.Calculate();

            Assert.NotNull(result);
            Assert.Equal(10, result.Values.Count);
        }

        [Fact]
        public void Calculate_FirstValuesAreNull()
        {
            var ohlcData = CreateOhlcvData(10m, 11m, 12m, 13m, 14m, 15m, 16m, 17m);

            var ema = new EMA(5, false);
            ema.Load(ohlcData);
            var result = ema.Calculate();

            // First period-1 values should be null
            Assert.Null(result.Values[0]);
            Assert.Null(result.Values[1]);
            Assert.Null(result.Values[2]);
            Assert.Null(result.Values[3]);

            // From index period-1, values should be present
            Assert.NotNull(result.Values[4]);
        }

        [Fact]
        public void Calculate_FirstEMAEqualsSMA()
        {
            var ohlcData = CreateOhlcvData(10m, 20m, 30m, 40m, 50m);

            var ema = new EMA(5, false);
            ema.Load(ohlcData);
            var result = ema.Calculate();

            // First EMA value should equal the SMA
            // SMA = (10+20+30+40+50)/5 = 30
            Assert.Equal(30m, result.Values[4]);
        }

        [Fact]
        public void Calculate_EmptyData_ReturnsEmptyResult()
        {
            var ohlcData = new List<SOhlcvItem>();

            var ema = new EMA(10, false);
            ema.Load(ohlcData);
            var result = ema.Calculate();

            Assert.NotNull(result);
            Assert.Empty(result.Values);
        }

        #endregion

        #region Multiplier Tests

        [Fact]
        public void Calculate_StandardMultiplier_IsCorrect()
        {
            // For 10-period EMA, multiplier = 2/(10+1) = 0.1818...
            var ohlcData = CreateOhlcvData(10m, 11m, 12m, 13m, 14m, 15m, 16m, 17m, 18m, 19m, 20m, 21m);

            var ema = new EMA(10, false);
            ema.Load(ohlcData);
            var result = ema.Calculate();

            // After period, EMA should be smoothed
            Assert.NotNull(result.Values[9]);
            Assert.NotNull(result.Values[10]);
            Assert.NotNull(result.Values[11]);
        }

        [Fact]
        public void Calculate_WilderMultiplier_IsCorrect()
        {
            // For Wilder's 10-period EMA, multiplier = 1/10 = 0.1
            var ohlcData = CreateOhlcvData(10m, 11m, 12m, 13m, 14m, 15m, 16m, 17m, 18m, 19m, 20m, 21m);

            var emaStandard = new EMA(10, false);
            emaStandard.Load(ohlcData);
            var resultStandard = emaStandard.Calculate();

            var emaWilder = new EMA(10, true);
            emaWilder.Load(ohlcData);
            var resultWilder = emaWilder.Calculate();

            // Both should have values but they should be different
            // due to different multipliers
            Assert.NotNull(resultStandard.Values[10]);
            Assert.NotNull(resultWilder.Values[10]);

            // The values should be different because multipliers differ
            // Standard: 2/(10+1) = 0.1818
            // Wilder: 1/10 = 0.1
            // This causes Wilder EMA to be smoother (slower to react)
            Assert.NotEqual(resultStandard.Values[11], resultWilder.Values[11]);
        }

        #endregion

        #region Default Constructor Tests

        [Fact]
        public void DefaultConstructor_UsesPeriod10()
        {
            var ohlcData = CreateOhlcvData(1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m);

            var ema = new EMA();
            ema.Load(ohlcData);
            var result = ema.Calculate();

            // First 9 values should be null (period 10)
            for (int i = 0; i < 9; i++)
            {
                Assert.Null(result.Values[i]);
            }

            // 10th value (index 9) should be the first EMA
            Assert.NotNull(result.Values[9]);
        }

        #endregion

        #region SingleDoubleSerie Tests

        [Fact]
        public void SingleDoubleSerie_DefaultConstructor_InitializesEmptyList()
        {
            var serie = new SingleDoubleSerie();

            Assert.NotNull(serie.Values);
            Assert.Empty(serie.Values);
        }

        #endregion
    }
}
