using Filtarr.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Filtarr.Core.Tests;

/// <summary>
/// A fresh in-memory database behind the real repositories, resolved through DI like the app does. Everything
/// resolved from one instance shares one scope (and so one database connection).
/// </summary>
public sealed class TestDb : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly ServiceProvider _provider;
    readonly IServiceScope _scope;

    public TestDb()
    {
        _conn.Open();
        _provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
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
