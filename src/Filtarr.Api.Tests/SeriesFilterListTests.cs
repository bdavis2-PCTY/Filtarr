using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

public class SeriesFilterListTests
{
    class FakeImdb(params ImdbTitle[] titles) : IImdbClient
    {
        public List<string> Lookups { get; } = new();
        public bool FailLookups { get; set; }
        public Task<List<ImdbTitle>> SearchSeriesAsync(string q, CancellationToken ct = default) =>
            Task.FromResult(titles.Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<ImdbTitle?> GetSeriesAsync(string id, CancellationToken ct = default)
        {
            Lookups.Add(id);
            return FailLookups ? throw new HttpRequestException("down") : Task.FromResult(titles.FirstOrDefault(t => t.ImdbId == id));
        }
    }

    static ImdbTitle Title(string id, string name) => new(id, name, 2010, "2010-", "Cast", $"img-{id}", "tvSeries");

    static SonarrSeries InLibrary(string imdb, string title, int year = 2015) =>
        new(7, title, year, true, null, null, null, null, 45, [], null, null, [new("poster", $"sonarr-{imdb}.jpg", null)], imdb);

    static Filter F(string? imdb, string? seriesTitle = null, bool enabled = true) => new()
    {
        Name = "f", ImdbId = imdb, SeriesTitle = seriesTitle, Enabled = enabled,
        Conditions = { new() { Field = "series.year", Operator = "gte", Value = "2000" } },
    };

    readonly FakeSonarr _sonarr = new();

    SeriesCatalog Create(FakeImdb imdb, params Filter[] filters) =>
        new(imdb, _sonarr, new FakeFilters(filters), new FakeSettings(new AppSettings { SonarrUrl = "http://sonarr:8989/" }), NullLogger<SeriesCatalog>.Instance);

    [Fact]
    public async Task Lists_only_series_that_have_filters_sorted_by_title_and_ignores_global_filters()
    {
        var imdb = new FakeImdb(Title("tt2", "Zeta"), Title("tt1", "Alpha"), Title("tt3", "NoFilters"));
        var list = await Create(imdb, F("tt2"), F("tt1"), F(null)).ListWithFiltersAsync();

        Assert.Equal(["Alpha", "Zeta"], list.Select(s => s.Title));
    }

    [Fact]
    public async Task Nothing_is_listed_when_no_series_has_filters()
    {
        var imdb = new FakeImdb(Title("tt1", "Alpha"));
        Assert.Empty(await Create(imdb, F(null)).ListWithFiltersAsync());
        Assert.Empty(imdb.Lookups);
    }

    [Fact]
    public async Task Counts_total_and_disabled_filters_per_series()
    {
        var imdb = new FakeImdb(Title("tt1", "Alpha"));
        var list = await Create(imdb, F("tt1"), F("tt1"), F("tt1", enabled: false)).ListWithFiltersAsync();

        var s = Assert.Single(list);
        Assert.Equal(3, s.FilterCount);
        Assert.Equal(1, s.DisabledFilterCount);
    }

    [Fact]
    public async Task Library_series_are_described_from_sonarr_without_an_imdb_lookup()
    {
        _sonarr.Series.Add(InLibrary("tt1", "Alpha (Sonarr)"));
        var imdb = new FakeImdb(Title("tt1", "Alpha (IMDb)"));

        var s = Assert.Single(await Create(imdb, F("tt1")).ListWithFiltersAsync());

        Assert.Equal("Alpha (Sonarr)", s.Title);
        Assert.True(s.InSonarr);
        Assert.Equal("sonarr-tt1.jpg", s.ImageUrl);
        Assert.Empty(imdb.Lookups);
    }

    [Fact]
    public async Task Series_outside_the_library_use_imdb_and_fall_back_to_the_stored_title()
    {
        var imdb = new FakeImdb(Title("tt1", "Alpha"));
        var list = await Create(imdb, F("tt1"), F("tt9", "Stored Title")).ListWithFiltersAsync();

        Assert.Equal(["Alpha", "Stored Title"], list.Select(s => s.Title));
        Assert.All(list, s => Assert.False(s.InSonarr));
        Assert.Equal("img-tt1", list[0].ImageUrl);

        imdb.FailLookups = true;
        var degraded = await Create(imdb, F("tt1", "Alpha")).ListWithFiltersAsync();
        Assert.Equal("Alpha", Assert.Single(degraded).Title);
    }

    [Fact]
    public async Task Sonarr_being_down_does_not_break_the_list()
    {
        _sonarr.FailLibrary = new HttpRequestException("down");
        var imdb = new FakeImdb(Title("tt1", "Alpha"));
        var s = Assert.Single(await Create(imdb, F("tt1")).ListWithFiltersAsync());
        Assert.False(s.InSonarr);
    }

    [Fact]
    public async Task Search_results_include_filter_counts_and_show_series_without_filters()
    {
        var imdb = new FakeImdb(Title("tt1", "Breaking Bad"), Title("tt2", "Breaking Point"));
        var results = await Create(imdb, F("tt1"), F("tt1"), F("tt1", enabled: false), F("tt99")).SearchAsync("breaking");

        Assert.Equal(["tt1", "tt2"], results.Select(r => r.ImdbId));
        Assert.Equal((3, 1), (results[0].FilterCount, results[0].DisabledFilterCount));
        Assert.Equal((0, 0), (results[1].FilterCount, results[1].DisabledFilterCount));
    }

    [Fact]
    public async Task Get_includes_the_filter_count()
    {
        var imdb = new FakeImdb(Title("tt1", "Alpha"));
        var info = await Create(imdb, F("tt1"), F("tt1")).GetAsync("tt1");
        Assert.Equal(2, info!.FilterCount);
    }
}

public sealed class SeriesFilterSummaryTests : IDisposable
{
    readonly SqliteConnection _conn = new("DataSource=:memory:");
    readonly AppDb _db;
    readonly FilterService _svc;

    public SeriesFilterSummaryTests()
    {
        _conn.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _svc = new FilterService(_db);
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }

    static Filter F(string? imdb, string? title = null, bool enabled = true) =>
        new() { Name = "f", ImdbId = imdb, SeriesTitle = title, Enabled = enabled, Conditions = { new() { Field = "series.year", Operator = "gte", Value = "1" } } };

    [Fact]
    public async Task Groups_by_series_counts_disabled_and_skips_global_filters()
    {
        await _svc.CreateAsync(F(null));
        await _svc.CreateAsync(F("tt2", "Zeta"));
        await _svc.CreateAsync(F("tt1", null));
        await _svc.CreateAsync(F("tt1", "Alpha", enabled: false));
        await _svc.CreateAsync(F("tt1", "Alpha"));

        var all = await _svc.SummarizeBySeriesAsync(null);

        Assert.Equal(["tt1", "tt2"], all.Select(s => s.ImdbId));          // sorted by title
        Assert.Equal(new SeriesFilterSummary("tt1", "Alpha", 3, 1), all[0]); // title taken from the first filter that has one
        Assert.Equal(new SeriesFilterSummary("tt2", "Zeta", 1, 0), all[1]);
    }

    [Fact]
    public async Task Can_be_limited_to_given_series_and_empty_input_returns_nothing()
    {
        await _svc.CreateAsync(F("tt1"));
        await _svc.CreateAsync(F("tt2"));

        Assert.Equal(["tt2"], (await _svc.SummarizeBySeriesAsync(["tt2", "tt3"])).Select(s => s.ImdbId));
        Assert.Empty(await _svc.SummarizeBySeriesAsync([]));
    }
}

public class ImdbTitleCacheTests
{
    [Fact]
    public async Task Title_lookups_are_cached_so_repeat_visits_do_not_call_imdb()
    {
        var handler = new StubHandler("""{"d":[{"id":"tt1","l":"Alpha","qid":"tvSeries","y":2010}]}""");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ImdbClient(new StubFactory(handler), new FiltarrOptions(), cache);

        Assert.Equal("Alpha", (await client.GetSeriesAsync("tt1"))!.Title);
        Assert.Equal("Alpha", (await client.GetSeriesAsync("tt1"))!.Title);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Misses_are_not_cached()
    {
        var handler = new StubHandler("""{"d":[]}""");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new ImdbClient(new StubFactory(handler), new FiltarrOptions(), cache);

        Assert.Null(await client.GetSeriesAsync("tt1"));
        Assert.Null(await client.GetSeriesAsync("tt1"));
        Assert.Equal(2, handler.Requests.Count);
    }
}
