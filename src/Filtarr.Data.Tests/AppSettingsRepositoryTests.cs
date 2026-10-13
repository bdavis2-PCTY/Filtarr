using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;

namespace Filtarr.Data.Tests;

public sealed class AppSettingsRepositoryTests : IDisposable
{
    readonly TestDb _db = new();
    readonly IAppSettingsRepository _repo;

    public AppSettingsRepositoryTests() => _repo = _db.Get<IAppSettingsRepository>();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Initialising_creates_the_default_row()
    {
        var s = await _repo.GetAsync();
        Assert.NotNull(s);
        Assert.Equal((AppSettings.DefaultSonarrUrl, "", true, 60), (s.SonarrUrl, s.ApiKey, s.ConfigImported, s.SyncIntervalMinutes));
    }

    [Fact]
    public async Task Save_overwrites_the_single_row_and_never_adds_another()
    {
        var s = (await _repo.GetAsync())!;
        s.ApiKey = "key";
        s.SonarrUrl = "http://nas:8989";
        s.LogLevel = "Information";
        await _repo.SaveAsync(s);

        var again = new AppSettings { ApiKey = "other", SyncIntervalMinutes = 5 };
        await _repo.SaveAsync(again);

        var saved = (await _repo.GetAsync())!;
        Assert.Equal(("other", AppSettings.DefaultSonarrUrl, null, 5), (saved.ApiKey, saved.SonarrUrl, saved.LogLevel, saved.SyncIntervalMinutes));
        Assert.Equal(1, saved.Id);
    }
}
