using CCXT.Collector.Kraken;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Tests.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CCXT.Collector.Tests.Exchanges
{
    /// <summary>
    /// Test suite for Kraken exchange WebSocket integration
    /// </summary>
    [Collection("Exchange Tests")]
    [Trait("Category", "Exchange")]
    [Trait("Exchange", "Kraken")]
    [Trait("Region", "US")]
    public class KrakenTests : WebSocketTestBase
    {
        private readonly ExchangeTestFixture _fixture;

        public KrakenTests(ITestOutputHelper output, ExchangeTestFixture fixture)
            : base(output, "Kraken")
        {
            _fixture = fixture;
            _testSymbols.Clear();
            _testSymbols.AddRange(_fixture.GetTestSymbols("Kraken"));
        }

        protected override IWebSocketClient CreateClient()
        {
            return new KrakenWebSocketClient();
        }

        protected override async Task<bool> ConnectClientAsync(IWebSocketClient client)
        {
            await client.ConnectAsync();
            return true;
        }

        protected override List<string> GetComprehensiveTestSymbols()
        {
            // Kraken uses standard symbol format with slash: BTC/USD, ETH/USD
            return new List<string>
            {
                "BTC/USD", "ETH/USD", "XRP/USD",
                "SOL/USD", "DOGE/USD", "ADA/USD"
            };
        }

        #region Test Methods

        [Fact]
        [Trait("Type", "Connection")]
        public async Task Kraken_WebSocket_Connection()
        {
            await TestWebSocketConnection();
            _fixture.MarkExchangeTested("Kraken", true);
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Kraken_Orderbook_Stream()
        {
            await TestOrderbookDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Kraken_Trade_Stream()
        {
            await TestTradeDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task Kraken_Ticker_Stream()
        {
            await TestTickerDataReception();
        }

        [Fact]
        [Trait("Type", "MultipleSubscriptions")]
        public async Task Kraken_Multiple_Subscriptions()
        {
            await TestMultipleSubscriptions();
        }

        #endregion
    }
}
