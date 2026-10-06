using System.Text.Json.Nodes;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

public class SeriesAdderTests
{
    readonly FakeSonarr _sonarr = new();

    SeriesAdder Create(AppSettings? settings = null) => new(_sonarr, new FakeSettings(settings ?? new AppSettings()));

    SeriesAdderTests Ready()
    {
        _sonarr.LookupResult = JsonNode.Parse("""
            {"title":"Breaking Bad","imdbId":"tt0903747","tvdbId":81189,"titleSlug":"breaking-bad","monitored":false,"qualityProfileId":0,
             "seasons":[{"seasonNumber":0,"monitored":true},{"seasonNumber":1,"monitored":true},{"seasonNumber":2,"monitored":true}]}
            """)!.AsObject();
        _sonarr.RootFolders.Add(new("M:\\TV", 100));
        _sonarr.RootFolders.Add(new("N:\\More", 999));
        _sonarr.Profiles.Add(new(1, "Main"));
        _sonarr.Profiles.Add(new(9, "High Quality"));
        return this;
    }

    [Fact]
    public async Task Adds_the_series_monitored_but_with_no_seasons_or_episodes_monitored_and_no_search()
    {
        Ready();
        var created = await Create().AddAsync("tt0903747");

        var body = Assert.Single(_sonarr.Added);
        Assert.Equal(true, (bool?)body["monitored"]);
        Assert.Equal("none", (string?)body["monitorNewItems"]);
        Assert.All(body["seasons"]!.AsArray(), s => Assert.Equal(false, (bool?)s!["monitored"]));
        var add = body["addOptions"]!;
        Assert.Equal("none", (string?)add["monitor"]);
        Assert.Equal(false, (bool?)add["searchForMissingEpisodes"]);
        Assert.Equal(false, (bool?)add["searchForCutoffUnmetEpisodes"]);
        Assert.Equal("breaking-bad", created.TitleSlug);
    }

    [Fact]
    public async Task Keeps_what_sonarrs_lookup_provided()
    {
        Ready();
        await Create().AddAsync("tt0903747");
        var body = _sonarr.Added.Single();
        Assert.Equal((81189, "Breaking Bad"), ((int?)body["tvdbId"], (string?)body["title"]));
    }

    [Fact]
    public async Task Defaults_to_the_first_root_folder_and_first_quality_profile()
    {
        Ready();
        await Create().AddAsync("tt0903747");
        var body = _sonarr.Added.Single();
        Assert.Equal(("M:\\TV", 1), ((string?)body["rootFolderPath"], (int?)body["qualityProfileId"]));
    }

    [Fact]
    public async Task Configured_root_folder_and_quality_profile_are_used()
    {
        Ready();
        await Create(new AppSettings { SonarrRootFolderPath = " N:\\Custom ", SonarrQualityProfile = " high quality " }).AddAsync("tt0903747");
        var body = _sonarr.Added.Single();
        Assert.Equal(("N:\\Custom", 9), ((string?)body["rootFolderPath"], (int?)body["qualityProfileId"]));
    }

    [Fact]
    public async Task Profile_names_are_compared_ignoring_stray_whitespace()
    {
        Ready();
        _sonarr.Profiles.Add(new(8, "Low quality "));
        await Create(new AppSettings { SonarrQualityProfile = "Low quality" }).AddAsync("tt0903747");
        Assert.Equal(8, (int?)_sonarr.Added.Single()["qualityProfileId"]);
    }

    [Fact]
    public async Task An_unknown_quality_profile_lists_the_available_ones()
    {
        Ready();
        var ex = await Assert.ThrowsAsync<SeriesAddException>(() => Create(new AppSettings { SonarrQualityProfile = "Nope" }).AddAsync("tt0903747"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("Nope", ex.Message);
        Assert.Contains("Main, High Quality", ex.Message);
        Assert.Empty(_sonarr.Added);
    }

    [Fact]
    public async Task Missing_root_folders_or_profiles_are_reported()
    {
        Ready();
        _sonarr.RootFolders.Clear();
        var noFolder = await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"));
        Assert.Contains("root folder", noFolder.Message);

        _sonarr.RootFolders.Add(new("M:\\TV", null));
        _sonarr.Profiles.Clear();
        var noProfile = await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"));
        Assert.Contains("quality profile", noProfile.Message);
    }

    [Fact]
    public async Task A_series_already_in_the_library_is_a_conflict()
    {
        Ready();
        _sonarr.Series.Add(new(1, "Breaking Bad", 2008, true, null, null, null, null, 45, [], null, null, null, "tt0903747"));
        var ex = await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Empty(_sonarr.Added);
    }

    [Fact]
    public async Task A_series_sonarr_cannot_look_up_is_not_found()
    {
        Ready();
        _sonarr.LookupResult = null;
        var ex = await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Sonarrs_rejection_message_is_passed_on()
    {
        Ready();
        _sonarr.FailAdd = new SonarrException("Path is invalid", 400);
        var ex = await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"));
        Assert.Equal((400, "Path is invalid"), (ex.StatusCode, ex.Message));

        _sonarr.FailAdd = new SonarrException("boom", 500);
        Assert.Equal(502, (await Assert.ThrowsAsync<SeriesAddException>(() => Create().AddAsync("tt0903747"))).StatusCode);
    }
}

public class SonarrLinkTests
{
    class NoImdb : IImdbClient
    {
        public Task<List<ImdbTitle>> SearchSeriesAsync(string q, CancellationToken ct = default) =>
            Task.FromResult(new List<ImdbTitle> { new("tt1", "Show", 2020, null, null, null, "tvSeries"), new("tt2", "Show 2", 2021, null, null, null, "tvSeries") });
        public Task<ImdbTitle?> GetSeriesAsync(string id, CancellationToken ct = default) => Task.FromResult<ImdbTitle?>(null);
    }

    [Fact]
    public async Task Search_results_link_to_sonarr_only_for_series_in_the_library_using_the_effective_url()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(new(1, "Show", 2020, true, null, null, null, null, 45, [], null, null, null, "tt1", "show-slug"));
        // A trailing slash on the saved URL must not double up.
        var settings = new FakeSettings(new AppSettings { SonarrUrl = "https://sonarr.example/" });
        var catalog = new SeriesCatalog(new NoImdb(), sonarr, new FakeFilters(), settings, NullLogger<SeriesCatalog>.Instance);

        var results = await catalog.SearchAsync("show");

        Assert.Equal("https://sonarr.example/series/show-slug", results[0].SonarrLink);
        Assert.Null(results[1].SonarrLink);
    }

    [Fact]
    public async Task A_series_without_a_slug_has_no_link()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(new(1, "Show", 2020, true, null, null, null, null, 45, [], null, null, null, "tt1"));
        var catalog = new SeriesCatalog(new NoImdb(), sonarr, new FakeFilters(), new FakeSettings(), NullLogger<SeriesCatalog>.Instance);
        Assert.Null((await catalog.SearchAsync("show"))[0].SonarrLink);
    }
}
