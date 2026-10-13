using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Filtarr.Data.Tests;

/// <summary>
/// A fresh in-memory database registered through <see cref="ServiceCollectionExtensions.AddFiltarrData(IServiceCollection, System.Data.Common.DbConnection)"/>
/// and initialised like the app does. Everything resolved from one instance shares one scope and one connection.
/// </summary>
public sealed class TestDb : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly ServiceProvider _provider;
    readonly IServiceScope _scope;

    public TestDb(IDictionary<string, string?>? configuration = null)
    {
        _conn.Open();
        _provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build())
            .AddFiltarrData(_conn)
            .BuildServiceProvider();
        _scope = _provider.CreateScope();
        Get<IDatabaseInitializer>().Initialize();
    }

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _conn.Dispose();
    }
}
