using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CCXT.Collector.Tests.Base
{
    /// <summary>
    /// Test collection for exchange tests to ensure they run sequentially
    /// to avoid WebSocket connection conflicts and rate limiting
    /// </summary>
    [CollectionDefinition("Exchange Tests")]
    public class ExchangeTestCollection : ICollectionFixture<ExchangeTestFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

    /// <summary>
    /// Shared test fixture for all exchange tests
    /// </summary>
    public class ExchangeTestFixture : IDisposable
    {
        public Dictionary<string, bool> TestedExchanges { get; }
        public DateTime TestStartTime { get; }
        public List<string> GlobalErrors { get; }
        
        // Common test symbols for different markets
        public Dictionary<string, List<string>> TestSymbolsByExchange { get; }

        public ExchangeTestFixture()
        {
            TestStartTime = DateTime.UtcNow;
            TestedExchanges = new Dictionary<string, bool>();
            GlobalErrors = new List<string>();
            TestSymbolsByExchange = InitializeTestSymbols();
            
            Console.WriteLine("=== Exchange Test Suite Started ===");
            Console.WriteLine($"Start Time: {TestStartTime:yyyy-MM-dd HH:mm:ss} UTC");
        }

        private Dictionary<string, List<string>> InitializeTestSymbols()
        {
            return new Dictionary<string, List<string>>
            {
                // Global exchanges - use common pairs
                ["Binance"] = new List<string> { "BTC/USDT", "ETH/USDT", "BNB/USDT" },
                ["MEXC"] = new List<string> { "BTC/USDT", "ETH/USDT", "MX/USDT" },
                ["Bybit"] = new List<string> { "BTC/USDT", "ETH/USDT", "SOL/USDT" },
                ["OKX"] = new List<string> { "BTC/USDT", "ETH/USDT", "XRP/USDT" },
                ["Bitget"] = new List<string> { "BTC/USDT", "ETH/USDT", "SOL/USDT" },
                ["Kucoin"] = new List<string> { "BTC/USDT", "ETH/USDT", "KCS/USDT" },
                ["Gate.io"] = new List<string> { "BTC/USDT", "ETH/USDT", "GT/USDT" },
                ["Huobi"] = new List<string> { "BTC/USDT", "ETH/USDT", "HT/USDT" },
                ["Crypto.com"] = new List<string> { "BTC/USDT", "ETH/USDT", "CRO/USDT" },
                
                // US exchanges
                ["Coinbase"] = new List<string> { "BTC/USD", "ETH/USD", "SOL/USD" },
                ["Kraken"] = new List<string> { "BTC/USD", "ETH/USD", "XRP/USD" },
                ["Bittrex"] = new List<string> { "BTC/USDT", "ETH/USDT", "ADA/USDT" },

                // GB (UK) exchanges
                ["Bitfinex"] = new List<string> { "BTC/USD", "ETH/USD", "XRP/USD" },
                ["Bitstamp"] = new List<string> { "BTC/USD", "ETH/USD", "XRP/USD" },

                // Korean exchanges - use KRW pairs
                ["Upbit"] = new List<string> { "BTC/KRW", "ETH/KRW", "XRP/KRW" },
                ["Bithumb"] = new List<string> { "BTC/KRW", "ETH/KRW", "XRP/KRW" },
                ["Coinone"] = new List<string> { "BTC/KRW", "ETH/KRW", "XRP/KRW" },
                ["Korbit"] = new List<string> { "BTC/KRW", "ETH/KRW", "XRP/KRW" },
                
                // Default for any exchange not listed
                ["Default"] = new List<string> { "BTC/USDT", "ETH/USDT" }
            };
        }

        public List<string> GetTestSymbols(string exchangeName)
        {
            return TestSymbolsByExchange.ContainsKey(exchangeName) 
                ? TestSymbolsByExchange[exchangeName] 
                : TestSymbolsByExchange["Default"];
        }

        public void MarkExchangeTested(string exchangeName, bool success)
        {
            TestedExchanges[exchangeName] = success;
        }

        public void Dispose()
        {
            var testDuration = DateTime.UtcNow - TestStartTime;
            Console.WriteLine("\n=== Exchange Test Suite Completed ===");
            Console.WriteLine($"Duration: {testDuration.TotalSeconds:F2} seconds");
            Console.WriteLine($"Exchanges Tested: {TestedExchanges.Count}");
            
            var successful = TestedExchanges.Count(kvp => kvp.Value);
            var failed = TestedExchanges.Count(kvp => !kvp.Value);
            
            Console.WriteLine($"✅ Successful: {successful}");
            Console.WriteLine($"❌ Failed: {failed}");
            
            if (GlobalErrors.Count > 0)
            {
                Console.WriteLine("\nGlobal Errors:");
                foreach (var error in GlobalErrors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }
        }
    }
}