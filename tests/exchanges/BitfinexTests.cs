using CCXT.Collector.Bitfinex;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Tests.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CCXT.Collector.Tests.Exchanges
{
    /// <summary>
    /// Test suite for Bitfinex exchange WebSocket integration
    /// </summary>
    [Collection("Exchange Tests")]
    [Trait("Category", "Exchange")]
    [Trait("Exchange", "Bitfinex")]
    [Trait("Region", "GB")]
    public class BitfinexTests : WebSocketTestBase
    {
        private readonly ExchangeTestFixture _fixture;

        public BitfinexTests(ITestOutputHelper output, ExchangeTestFixture fixture)
            : base(output, "Bitfinex")
        {
            _fixture = fixture;
            _testSymbols.Clear();
            _testSymbols.AddRange(_fixture.GetTestSymbols("Bitfinex"));
        }

        protected override IWebSocketClient CreateClient()
        {
            return new BitfinexWebSocketClient();
        }

        protected override async Task<bool> ConnectClientAsync(IWebSocketClient client)
        {
            await client.ConnectAsync();
            return true;
        }

        protected override List<string> GetComprehensiveTestSymbols()
        {
            // Bitfinex uses USD pairs primarily
            return new List<string>
            {
                "BTC/USD", "ETH/USD", "XRP/USD",
                "SOL/USD", "LTC/USD", "EOS/USD"
            };
        }

        #region Test Methods

        [Fact]
        [Trait("Type", "Connection")]
        public async Task Bitfinex_WebSocket_Connection()
        {
            await TestWebSocketConnection();
            _fixture.MarkExchangeTested("Bitfinex", true);
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitfinex_Orderbook_Stream()
        {
            await TestOrderbookDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitfinex_Trade_Stream()
        {
            await TestTradeDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Bitfinex_Ticker_Stream()
        {
            await TestTickerDataReception();
        }

        [Fact]
        [Trait("Type", "MultipleSubscriptions")]
        public async Task Bitfinex_Multiple_Subscriptions()
        {
            await TestMultipleSubscriptions();
        }

        #endregion
    }
}
