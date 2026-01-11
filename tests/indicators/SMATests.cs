using CCXT.Collector.Indicator;
using CCXT.Collector.Service;
using System.Collections.Generic;
using Xunit;

namespace CCXT.Collector.Tests.Indicators
{
    /// <summary>
    /// Unit tests for SMA (Simple Moving Average) indicator
    /// </summary>
    public class SMATests
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
        public void Calculate_5DaySMA_ReturnsCorrectValues()
        {
            // Example from stockcharts.com:
            // Daily Closing Prices: 11,12,13,14,15,16,17
            // First day of 5-day SMA: (11 + 12 + 13 + 14 + 15) / 5 = 13
            // Second day of 5-day SMA: (12 + 13 + 14 + 15 + 16) / 5 = 14
            // Third day of 5-day SMA: (13 + 14 + 15 + 16 + 17) / 5 = 15
            var ohlcData = CreateOhlcvData(11m, 12m, 13m, 14m, 15m, 16m, 17m);

            var sma = new SMA(5);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Equal(7, result.Values.Count);

            // First 4 values should be null (not enough data for 5-period SMA)
            Assert.Null(result.Values[0]);
            Assert.Null(result.Values[1]);
            Assert.Null(result.Values[2]);
            Assert.Null(result.Values[3]);

            // Index 4: (11+12+13+14+15)/5 = 13
            Assert.Equal(13m, result.Values[4]);

            // Index 5: (12+13+14+15+16)/5 = 14
            Assert.Equal(14m, result.Values[5]);

            // Index 6: (13+14+15+16+17)/5 = 15
            Assert.Equal(15m, result.Values[6]);
        }

        [Fact]
        public void Calculate_3DaySMA_ReturnsCorrectValues()
        {
            var ohlcData = CreateOhlcvData(10m, 20m, 30m, 40m, 50m);

            var sma = new SMA(3);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Equal(5, result.Values.Count);

            // First 2 values should be null
            Assert.Null(result.Values[0]);
            Assert.Null(result.Values[1]);

            // Index 2: (10+20+30)/3 = 20
            Assert.Equal(20m, result.Values[2]);

            // Index 3: (20+30+40)/3 = 30
            Assert.Equal(30m, result.Values[3]);

            // Index 4: (30+40+50)/3 = 40
            Assert.Equal(40m, result.Values[4]);
        }

        [Fact]
        public void Calculate_1DaySMA_ReturnsAllValues()
        {
            var ohlcData = CreateOhlcvData(100m, 200m, 300m);

            var sma = new SMA(1);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Equal(3, result.Values.Count);

            // All values should be present (1-period SMA equals the close price)
            Assert.Equal(100m, result.Values[0]);
            Assert.Equal(200m, result.Values[1]);
            Assert.Equal(300m, result.Values[2]);
        }

        [Fact]
        public void Calculate_PeriodEqualToDataLength_ReturnsOneValue()
        {
            var ohlcData = CreateOhlcvData(10m, 20m, 30m);

            var sma = new SMA(3);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Equal(3, result.Values.Count);

            Assert.Null(result.Values[0]);
            Assert.Null(result.Values[1]);
            Assert.Equal(20m, result.Values[2]); // (10+20+30)/3 = 20
        }

        [Fact]
        public void Calculate_PeriodLongerThanData_ReturnsAllNulls()
        {
            var ohlcData = CreateOhlcvData(10m, 20m);

            var sma = new SMA(5);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Equal(2, result.Values.Count);
            Assert.Null(result.Values[0]);
            Assert.Null(result.Values[1]);
        }

        [Fact]
        public void Calculate_EmptyData_ReturnsEmptyResult()
        {
            var ohlcData = new List<SOhlcvItem>();

            var sma = new SMA(5);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.NotNull(result);
            Assert.Empty(result.Values);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Calculate_WithDecimalPrices_ReturnsCorrectAverage()
        {
            var ohlcData = CreateOhlcvData(10.5m, 20.7m, 30.3m);

            var sma = new SMA(3);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            // (10.5+20.7+30.3)/3 = 61.5/3 = 20.5
            Assert.Equal(20.5m, result.Values[2]);
        }

        [Fact]
        public void Calculate_SingleDataPoint_ReturnsCorrectValue()
        {
            var ohlcData = CreateOhlcvData(100m);

            var sma = new SMA(1);
            sma.Load(ohlcData);
            var result = sma.Calculate();

            Assert.Single(result.Values);
            Assert.Equal(100m, result.Values[0]);
        }

        #endregion
    }
}
