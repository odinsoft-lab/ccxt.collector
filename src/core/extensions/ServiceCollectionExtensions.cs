using System;
using CCXT.Collector.Core.Abstractions;
using CCXT.Collector.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CCXT.Collector.Core.Extensions
{
    /// <summary>
    /// Extension methods for IServiceCollection to register CCXT.Collector services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds CCXT.Collector core services to the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddCcxtCollector(this IServiceCollection services)
        {
            // Register the channel observer as singleton
            services.AddSingleton<IChannelObserver, ChannelObserver>();

            return services;
        }

        /// <summary>
        /// Adds CCXT.Collector with a custom channel observer
        /// </summary>
        /// <typeparam name="TObserver">Custom observer type implementing IChannelObserver</typeparam>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddCcxtCollector<TObserver>(this IServiceCollection services)
            where TObserver : class, IChannelObserver
        {
            services.AddSingleton<IChannelObserver, TObserver>();

            return services;
        }

        /// <summary>
        /// Adds a specific exchange WebSocket client to the service collection
        /// </summary>
        /// <typeparam name="TClient">WebSocket client type</typeparam>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddExchangeClient<TClient>(this IServiceCollection services)
            where TClient : class, IWebSocketClient
        {
            services.AddTransient<TClient>();
            services.AddTransient<IWebSocketClient, TClient>();

            return services;
        }

        /// <summary>
        /// Adds a specific exchange WebSocket client with factory configuration
        /// </summary>
        /// <typeparam name="TClient">WebSocket client type</typeparam>
        /// <param name="services">The service collection</param>
        /// <param name="configure">Factory method to configure the client</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddExchangeClient<TClient>(
            this IServiceCollection services,
            Func<IServiceProvider, TClient> configure)
            where TClient : class, IWebSocketClient
        {
            services.AddTransient(configure);
            services.AddTransient<IWebSocketClient>(configure);

            return services;
        }

        /// <summary>
        /// Adds multiple exchange clients as a keyed service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configureExchanges">Action to configure exchanges</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddExchangeClients(
            this IServiceCollection services,
            Action<ExchangeClientBuilder> configureExchanges)
        {
            var builder = new ExchangeClientBuilder(services);
            configureExchanges(builder);

            return services;
        }
    }

    /// <summary>
    /// Builder class for configuring multiple exchange clients
    /// </summary>
    public class ExchangeClientBuilder
    {
        private readonly IServiceCollection _services;

        internal ExchangeClientBuilder(IServiceCollection services)
        {
            _services = services;
        }

        /// <summary>
        /// Adds an exchange client by type
        /// </summary>
        /// <typeparam name="TClient">WebSocket client type</typeparam>
        /// <returns>The builder for chaining</returns>
        public ExchangeClientBuilder AddClient<TClient>()
            where TClient : class, IWebSocketClient
        {
            _services.AddTransient<TClient>();
            return this;
        }

        /// <summary>
        /// Adds an exchange client with configuration
        /// </summary>
        /// <typeparam name="TClient">WebSocket client type</typeparam>
        /// <param name="configure">Factory method to configure the client</param>
        /// <returns>The builder for chaining</returns>
        public ExchangeClientBuilder AddClient<TClient>(Func<IServiceProvider, TClient> configure)
            where TClient : class, IWebSocketClient
        {
            _services.AddTransient(configure);
            return this;
        }
    }
}
