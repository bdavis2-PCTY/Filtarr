using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Clients.Models;
using Filtarr.Core.Services;

namespace Filtarr.Core.Tests;

public class ConnectionTestServiceTests
{
    readonly FakeSonarr _sonarr = new();

    [Fact]
    public async Task A_failed_sonarr_connection_is_reported_not_thrown()
    {
        _sonarr.FailTest = new InvalidOperationException("nope");
        var result = await new ConnectionTestService(_sonarr, new FakeOmdbClient()).TestSonarrAsync(" http://s:8989 ", " key ");

        Assert.Equal((false, "nope"), (result.Ok, result.Message));
        Assert.Equal(("http://s:8989", "key"), Assert.Single(_sonarr.Tests));
    }

    [Fact]
    public async Task A_good_sonarr_connection_says_what_it_connected_to_and_blank_input_means_the_saved_values()
    {
        var result = await new ConnectionTestService(_sonarr, new FakeOmdbClient()).TestSonarrAsync("  ", null);

        Assert.True(result.Ok);
        Assert.StartsWith("Connected to ", result.Message);
        Assert.Equal((null, null), Assert.Single(_sonarr.Tests));
    }

    [Fact]
    public async Task Sonarr_options_list_root_folder_paths_and_trimmed_profile_names()
    {
        _sonarr.RootFolders.Add(new("M:\\TV", 10));
        _sonarr.Profiles.Add(new(1, " HD "));

        var options = await new ConnectionTestService(_sonarr, new FakeOmdbClient()).GetSonarrOptionsAsync();

        Assert.Equal(["M:\\TV"], options.RootFolders);
        Assert.Equal(["HD"], options.QualityProfiles);
    }

    [Fact]
    public async Task The_omdb_result_is_passed_through()
    {
        var result = await new ConnectionTestService(_sonarr, new FakeOmdbClient(false, "bad key")).TestOmdbAsync("k");
        Assert.Equal((false, "bad key"), (result.Ok, result.Message));
    }
}

public class MetadataServiceTests
{
    [Fact]
    public async Task The_library_is_listed_by_title_and_the_fields_come_from_the_engine()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(new(2, "Zeta", 2020, true, null, null, null, null, 0, null, null, null, null));
        sonarr.Series.Add(new(1, "Alpha", 2010, false, null, null, null, null, 0, null, null, null, null));
        var svc = new MetadataService(sonarr);

        var list = await svc.ListSonarrSeriesAsync();

        Assert.Equal(["Alpha", "Zeta"], list.Select(s => s.Title));
        Assert.Equal((1, 2010, false), (list[0].Id, list[0].Year, list[0].Monitored));
        Assert.Contains(svc.GetFields(), f => f.Key == "episode.rating");
    }
}

class FakeOmdbClient(bool ok = true, string message = "ok") : IOmdbClient
{
    public Task<List<OmdbEpisode>> GetSeasonAsync(string seriesImdbId, int season, CancellationToken ct = default) => Task.FromResult(new List<OmdbEpisode>());
    public Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default) => Task.FromResult(new OmdbTestResult(ok, message));
    public Task<OmdbTitle?> GetTitleAsync(string imdbId, CancellationToken ct = default) => Task.FromResult<OmdbTitle?>(null);
}
