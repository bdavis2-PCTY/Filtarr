using Filtarr.Core.Catalogs;
using Filtarr.Core.Catalogs.Interfaces;
using Filtarr.Core.Clients;
using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Configuration;
using Filtarr.Core.Filtering;
using Filtarr.Core.Filtering.Interfaces;
using Filtarr.Core.Services;
using Filtarr.Core.Services.Interfaces;
using Filtarr.Core.Synchronization;
using Filtarr.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Filtarr.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the API needs: the database (see <see cref="Filtarr.Data.ServiceCollectionExtensions"/>), the clients,
    /// the filter engine, the catalogs and the services. The host must also register a <c>LoggingLevelSwitch</c> (the settings
    /// service drives it), an <c>IConfiguration</c> and logging. Call <see cref="InitializeFiltarr"/> once the provider is built.
    /// </summary>
    public static IServiceCollection AddFiltarrCore(this IServiceCollection services, string databasePath, ExternalApiOptions options)
    {
        services.AddFiltarrData(databasePath);
        services.AddSingleton(options);

        services.AddMemoryCache();
        services.AddHttpClient("sonarr");
        services.AddHttpClient("imdb", c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.UserAgent.ParseAdd("Filtarr"); });
        // Short timeout + short-lived idle connections: OMDb drops idle keep-alive connections, and reusing a dead one hangs until the timeout.
        services.AddHttpClient("omdb", c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SyncGate>();
        services.AddSingleton<IFilterEngine, FilterEngine>();

        services.AddScoped<ISonarrClient, SonarrClient>();
        services.AddScoped<IOmdbClient, OmdbClient>();
        services.AddScoped<IImdbClient, ImdbClient>();

        services.AddScoped<ISeriesCatalog, SeriesCatalog>();

        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ISettingsUpdater, SettingsUpdater>();
        services.AddScoped<IConnectionTestService, ConnectionTestService>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<IFilterService, FilterService>();
        services.AddScoped<IQuickFilterService, QuickFilterService>();
        services.AddScoped<IEpisodeRatingService, EpisodeRatingService>();
        services.AddScoped<IMonitoredHistoryService, MonitoredHistoryService>();
        services.AddScoped<ISeriesDetailsService, SeriesDetailsService>();
        services.AddScoped<ISeriesAdder, SeriesAdder>();
        services.AddScoped<ISyncService, SyncService>();
        return services;
    }

    /// <summary>Creates or upgrades the database and imports legacy configuration. Safe to call on every start.</summary>
    public static void InitializeFiltarr(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().Initialize();
    }
}
