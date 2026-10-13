using Filtarr.Data.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Filtarr.Data.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void Every_repository_and_the_initializer_resolve_from_the_container()
    {
        using var db = new TestDb();

        Assert.NotNull(db.Get<IAppSettingsRepository>());
        Assert.NotNull(db.Get<IFilterRepository>());
        Assert.NotNull(db.Get<IQuickFilterRepository>());
        Assert.NotNull(db.Get<ISyncRunRepository>());
        Assert.NotNull(db.Get<IMonitoredEpisodeRepository>());
        Assert.NotNull(db.Get<ISeasonRatingsRepository>());
        Assert.NotNull(db.Get<IDatabaseInitializer>());
    }

    [Fact]
    public async Task A_database_file_is_created_and_initialised_on_demand()
    {
        var path = Path.Combine(Path.GetTempPath(), $"filtarr-{Guid.NewGuid():N}.db");
        try
        {
            using (var provider = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
                .AddFiltarrData(path)
                .BuildServiceProvider())
            {
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().Initialize();
                Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>().GetAsync());
            }
            Assert.True(File.Exists(path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // release the file so it can be deleted
            File.Delete(path);
        }
    }
}
