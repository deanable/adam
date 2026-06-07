using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using LiquidVision.Core.Configuration;

namespace LiquidVision.Core.DependencyInjection;

/// <summary>Dependency-injection helpers for registering the LiquidVision analyzer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>HTTP client logical name used for model downloads.</summary>
    public const string HttpClientName = "LiquidVision";

    /// <summary>
    /// Registers <see cref="ILiquidVisionAnalyzer"/> (as a singleton) along with a configured
    /// <see cref="IHttpClientFactory"/> client used for model downloads.
    /// </summary>
    public static IServiceCollection AddLiquidVision(
        this IServiceCollection services, Action<LiquidVisionOptions>? configure = null)
    {
        var options = new LiquidVisionOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddHttpClient(HttpClientName, client => client.Timeout = options.DownloadTimeout);

        services.AddSingleton<ILiquidVisionAnalyzer>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new LiquidVisionAnalyzer(options, factory);
        });

        return services;
    }
}
