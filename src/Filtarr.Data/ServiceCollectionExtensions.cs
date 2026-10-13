using System.Data.Common;
using Filtarr.Data.Repositories;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Filtarr.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database and one repository per table, backed by the SQLite file at <paramref name="databasePath"/>
    /// (its folder must exist). Everything is scoped. The host also needs an <c>IConfiguration</c> and logging.
    /// </summary>
    public static IServiceCollection AddFiltarrData(this IServiceCollection services, string databasePath) =>
        services.Register(o => o.UseSqlite($"Data Source={databasePath}"));

    /// <summary>
    /// As <see cref="AddFiltarrData(IServiceCollection, string)"/> but on an existing connection, which the caller owns and opens
    /// (e.g. a shared in-memory SQLite connection in tests).
    /// </summary>
    public static IServiceCollection AddFiltarrData(this IServiceCollection services, DbConnection connection) =>
        services.Register(o => o.UseSqlite(connection));

    static IServiceCollection Register(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<AppDb>(configure);
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
        services.AddScoped<IFilterRepository, FilterRepository>();
        services.AddScoped<IQuickFilterRepository, QuickFilterRepository>();
        services.AddScoped<ISyncRunRepository, SyncRunRepository>();
        services.AddScoped<IMonitoredEpisodeRepository, MonitoredEpisodeRepository>();
        services.AddScoped<ISeasonRatingsRepository, SeasonRatingsRepository>();
        return services;
    }
}
