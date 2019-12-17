namespace Portal.Infrastructure.ServiceTools
{
    using Microsoft.Extensions.Options;
    using Polly;
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Cache;
    using Polly.Retry;

    public interface IServiceAccessHelper
    {
        Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, string cacheKey = null) where TResult : class;
    }

    [ExcludeFromCodeCoverage]
    public class ServiceAccessHelper : IServiceAccessHelper
    {
        private readonly IServiceResponseMemoryCache memoryCache;
        private readonly AsyncRetryPolicy retryPolicy;
        private readonly int defaultRetry = 3;
        private readonly int defaultDelay = 2;

        public ServiceAccessHelper(IServiceResponseMemoryCache memoryCache, IOptions<ServiceAccessOptions> options)
        {
            this.memoryCache = memoryCache;

            int retry = this.defaultRetry;
            int delay = this.defaultDelay;

            if (options?.Value != null)
            {
                retry = options.Value.Retry;
                delay = options.Value.Delay;
            }

            this.retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<WebException>()
                .WaitAndRetryAsync(retry,
                    x => TimeSpan.FromSeconds(delay));
        }

        public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, string cacheKey) where TResult : class
        {
            if (!string.IsNullOrWhiteSpace(cacheKey))
            {
                TResult cacheResponse = this.GetValueOnCache<TResult>(cacheKey);

                if (cacheResponse != null)
                {
                    return cacheResponse;
                }

                var providerResponse = await this.retryPolicy.ExecuteAsync(action);
                return this.SetValueOnCache(cacheKey, providerResponse);
            }

            return await this.retryPolicy.ExecuteAsync(action);
        }

        private T SetValueOnCache<T>(string cacheKey, T response) where T : class
        {
            if (!string.IsNullOrWhiteSpace(cacheKey) && response != null)
            {
                this.memoryCache.SetOnCache(cacheKey, response);
            }

            return response;
        }

        private T GetValueOnCache<T>(string cacheKey) where T : class
        {
            if (!string.IsNullOrWhiteSpace(cacheKey))
            {
                bool isInCache = this.memoryCache.TryGetValue(cacheKey, out T response);

                if (isInCache && response != null)
                {
                    return response;
                }
            }

            return default(T);
        }
    }
}
