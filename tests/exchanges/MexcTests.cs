using CCXT.Collector.Mexc;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Tests.Base;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CCXT.Collector.Tests.Exchanges
{
    /// <summary>
    /// Test suite for MEXC exchange WebSocket integration
    /// </summary>
    [Collection("Exchange Tests")]
    [Trait("Category", "Exchange")]
    [Trait("Exchange", "MEXC")]
    [Trait("Region", "CN")]
    public class MexcTests : WebSocketTestBase
    {
        private readonly ExchangeTestFixture _fixture;

        public MexcTests(ITestOutputHelper output, ExchangeTestFixture fixture)
            : base(output, "MEXC")
        {
            _fixture = fixture;
            _testSymbols.Clear();
            _testSymbols.AddRange(_fixture.GetTestSymbols("MEXC"));
        }

        protected override IWebSocketClient CreateClient()
        {
            return new MexcWebSocketClient();
        }

        protected override async Task<bool> ConnectClientAsync(IWebSocketClient client)
        {
            await client.ConnectAsync();
            return true;
        }

        protected override List<string> GetComprehensiveTestSymbols()
        {
            // MEXC uses USDT pairs primarily
            return new List<string>
            {
                "BTC/USDT", "ETH/USDT", "XRP/USDT",
                "SOL/USDT", "DOGE/USDT", "MX/USDT"
            };
        }

        #region Test Methods

        [Fact]
        [Trait("Type", "Connection")]
        public async Task MEXC_WebSocket_Connection()
        {
            await TestWebSocketConnection();
            _fixture.MarkExchangeTested("MEXC", true);
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task MEXC_Orderbook_Stream()
        {
            await TestOrderbookDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task MEXC_Trade_Stream()
        {
            await TestTradeDataReception();
        }

        [Fact]
        [Trait("Type", "DataStream")]
        public async Task MEXC_Ticker_Stream()
        {
            await TestTickerDataReception();
        }

        [Fact]
        [Trait("Type", "MultipleSubscriptions")]
        public async Task MEXC_Multiple_Subscriptions()
        {
            await TestMultipleSubscriptions();
        }

        #endregion
    }
}
