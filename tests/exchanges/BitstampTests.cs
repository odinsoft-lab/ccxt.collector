using CCXT.Collector.Bitstamp;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Tests.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CCXT.Collector.Tests.Exchanges
{
    /// <summary>
    /// Test suite for Bitstamp exchange WebSocket integration
    /// </summary>
    [Collection("Exchange Tests")]
    [Trait("Category", "Exchange")]
    [Trait("Exchange", "Bitstamp")]
    [Trait("Region", "GB")]
    public class BitstampTests : WebSocketTestBase
    {
        private readonly ExchangeTestFixture _fixture;

        public BitstampTests(ITestOutputHelper output, ExchangeTestFixture fixture)
            : base(output, "Bitstamp")
        {
            _fixture = fixture;
            _testSymbols.Clear();
            _testSymbols.AddRange(_fixture.GetTestSymbols("Bitstamp"));
        }

        protected override IWebSocketClient CreateClient()
        {
            return new BitstampWebSocketClient();
        }

        protected override async Task<bool> ConnectClientAsync(IWebSocketClient client)
        {
            await client.ConnectAsync();
            return true;
        }

        protected override List<string> GetComprehensiveTestSymbols()
        {
            // Bitstamp uses USD, EUR, GBP pairs primarily
            return new List<string>
            {
                "BTC/USD", "ETH/USD", "XRP/USD",
                "BTC/EUR", "ETH/EUR", "LTC/USD"
            };
        }

        #region Test Methods

        [Fact]
        [Trait("Type", "Connection")]
        public async Task Bitstamp_WebSocket_Connection()
        {
            await TestWebSocketConnection();
            _fixture.MarkExchangeTested("Bitstamp", true);
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitstamp_Orderbook_Stream()
        {
            await TestOrderbookDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitstamp_Trade_Stream()
        {
            await TestTradeDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitstamp_Ticker_Stream()
        {
            await TestTickerDataReception();
        }

        [Fact]
        [Trait("Type", "MultipleSubscriptions")]
        public async Task Bitstamp_Multiple_Subscriptions()
        {
            await TestMultipleSubscriptions();
        }

        #endregion
    }
}
