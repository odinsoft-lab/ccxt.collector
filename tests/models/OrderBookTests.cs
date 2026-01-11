using CCXT.Collector.Service;
using System.Collections.Generic;
using Xunit;

namespace CCXT.Collector.Tests.Models
{
    /// <summary>
    /// Unit tests for OrderBook models
    /// </summary>
    public class OrderBookTests
    {
        #region SOrderBookItem Tests

        [Fact]
        public void SOrderBookItem_DefaultValues_AreZeroOrNull()
        {
            var item = new SOrderBookItem();

            Assert.Equal(0m, item.quantity);
            Assert.Equal(0m, item.price);
            Assert.Equal(0m, item.amount);
            Assert.Equal(0, item.count);
            Assert.Equal(0L, item.id);
            Assert.Null(item.action);
        }

        [Fact]
        public void SOrderBookItem_CanSetProperties()
        {
            var item = new SOrderBookItem
            {
                action = "I",
                quantity = 1.5m,
                price = 50000m,
                amount = 75000m,
                count = 10,
                id = 123456
            };

            Assert.Equal("I", item.action);
            Assert.Equal(1.5m, item.quantity);
            Assert.Equal(50000m, item.price);
            Assert.Equal(75000m, item.amount);
            Assert.Equal(10, item.count);
            Assert.Equal(123456L, item.id);
        }

        #endregion

        #region SOrderBookData Tests

        [Fact]
        public void SOrderBookData_DefaultConstructor_InitializesLists()
        {
            var data = new SOrderBookData();

            Assert.NotNull(data.asks);
            Assert.NotNull(data.bids);
            Assert.Empty(data.asks);
            Assert.Empty(data.bids);
            Assert.Equal(0m, data.askSumQty);
            Assert.Equal(0m, data.bidSumQty);
            Assert.Equal(0L, data.timestamp);
        }

        [Fact]
        public void SOrderBookData_CanAddBidsAndAsks()
        {
            var data = new SOrderBookData();

            data.bids.Add(new SOrderBookItem { price = 50000m, quantity = 1.0m });
            data.bids.Add(new SOrderBookItem { price = 49999m, quantity = 2.0m });
            data.asks.Add(new SOrderBookItem { price = 50001m, quantity = 0.5m });
            data.asks.Add(new SOrderBookItem { price = 50002m, quantity = 1.5m });

            Assert.Equal(2, data.bids.Count);
            Assert.Equal(2, data.asks.Count);
            Assert.Equal(50000m, data.bids[0].price);
            Assert.Equal(50001m, data.asks[0].price);
        }

        [Fact]
        public void SOrderBookData_CanSetTimestamp()
        {
            var data = new SOrderBookData();
            data.timestamp = 1700000000000L;

            Assert.Equal(1700000000000L, data.timestamp);
        }

        [Fact]
        public void SOrderBookData_CanSetSumQuantities()
        {
            var data = new SOrderBookData
            {
                askSumQty = 100.5m,
                bidSumQty = 200.5m
            };

            Assert.Equal(100.5m, data.askSumQty);
            Assert.Equal(200.5m, data.bidSumQty);
        }

        #endregion

        #region SOrderBook Tests

        [Fact]
        public void SOrderBook_CanSetResult()
        {
            var orderbook = new SOrderBook();
            var data = new SOrderBookData();
            data.bids.Add(new SOrderBookItem { price = 50000m, quantity = 1.0m });

            orderbook.result = data;
            orderbook.symbol = "BTC/USDT";
            orderbook.exchange = "binance";

            Assert.Equal("BTC/USDT", orderbook.symbol);
            Assert.Equal("binance", orderbook.exchange);
            Assert.NotNull(orderbook.result);
            Assert.Single(orderbook.result.bids);
        }

        #endregion

        #region OrderBook Validation Helpers

        [Fact]
        public void BidsOrderValidation_DescendingOrder_IsValid()
        {
            var data = new SOrderBookData();
            data.bids.Add(new SOrderBookItem { price = 50003m });
            data.bids.Add(new SOrderBookItem { price = 50002m });
            data.bids.Add(new SOrderBookItem { price = 50001m });
            data.bids.Add(new SOrderBookItem { price = 50000m });

            // Verify bids are in descending order
            for (int i = 1; i < data.bids.Count; i++)
            {
                Assert.True(data.bids[i - 1].price >= data.bids[i].price);
            }
        }

        [Fact]
        public void AsksOrderValidation_AscendingOrder_IsValid()
        {
            var data = new SOrderBookData();
            data.asks.Add(new SOrderBookItem { price = 50004m });
            data.asks.Add(new SOrderBookItem { price = 50005m });
            data.asks.Add(new SOrderBookItem { price = 50006m });
            data.asks.Add(new SOrderBookItem { price = 50007m });

            // Verify asks are in ascending order
            for (int i = 1; i < data.asks.Count; i++)
            {
                Assert.True(data.asks[i - 1].price <= data.asks[i].price);
            }
        }

        [Fact]
        public void SpreadValidation_BestAskGreaterThanBestBid()
        {
            var data = new SOrderBookData();
            data.bids.Add(new SOrderBookItem { price = 50000m });
            data.asks.Add(new SOrderBookItem { price = 50001m });

            var spread = data.asks[0].price - data.bids[0].price;

            Assert.True(spread > 0);
            Assert.Equal(1m, spread);
        }

        #endregion
    }
}
