using System.Net;
using System.Text.Json.Nodes;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

public sealed class SeriesDetailsTests : IDisposable
{
    class FakeImdb : IImdbClient
    {
        public ImdbTitle? Title { get; set; } = new("tt1", "Severance", 2022, "2022-", "Adam Scott, Britt Lower", "imdb-poster.jpg", "tvSeries");
        public Exception? Fail { get; set; }
        public Task<List<ImdbTitle>> SearchSeriesAsync(string q, CancellationToken ct = default) => Task.FromResult(new List<ImdbTitle>());
        public Task<ImdbTitle?> GetSeriesAsync(string id, CancellationToken ct = default) => Fail is not null ? throw Fail : Task.FromResult(Title);
    }

    class FakeOmdb : IOmdbClient
    {
        public OmdbTitle? Title { get; set; }
        public Exception? Fail { get; set; }
        public int Calls { get; private set; }
        public Task<List<OmdbEpisode>> GetSeasonAsync(string id, int season, CancellationToken ct = default) => Task.FromResult(new List<OmdbEpisode>());
        public Task<OmdbTestResult> TestKeyAsync(string? apiKey, CancellationToken ct = default) => Task.FromResult(new OmdbTestResult(true, "ok"));
        public Task<OmdbTitle?> GetTitleAsync(string id, CancellationToken ct = default)
        {
            Calls++;
            return Fail is not null ? throw Fail : Task.FromResult(Title);
        }
    }

    readonly FakeImdb _imdb = new();
    readonly FakeOmdb _omdb = new();
    readonly FakeSonarr _sonarr = new();
    readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    SeriesDetailsService Create() => new(_imdb, _omdb, _sonarr, _cache, NullLogger<SeriesDetailsService>.Instance);

    static OmdbTitle FullOmdb() => new(
        "The full IMDb plot.", ["Adam Scott", "Zach Cherry", "Britt Lower"], ["Drama", "Mystery"], "TV-MA", 50, "18 Feb 2022",
        "English", "United States", "Won 2 Emmys", 8.7, 520000, 2);

    static SonarrSeries InLibrary() => new(
        42, "Severance", 2022, true, "continuing", "Apple TV", "standard", "TV-14", 49, ["Sci-Fi"], new SonarrRatings(8.4, 1000), new SonarrLanguage("English"),
        [new("poster", "sonarr-poster.jpg", null), new("fanart", "fanart.jpg", null)], "tt1", "severance",
        "Sonarr overview.", [11, 99], new SonarrStatistics(2, 18, 5, 40), "2022-02-18T00:00:00Z");

    [Fact]
    public async Task Omdb_provides_plot_cast_genres_and_rating_and_sonarr_the_rest()
    {
        _omdb.Title = FullOmdb();
        _sonarr.Series.Add(InLibrary());
        _sonarr.Tags.AddRange([new(11, "favourites"), new(12, "unused")]);

        var d = (await Create().GetAsync("tt1"))!;

        Assert.Equal("The full IMDb plot.", d.Plot);
        Assert.Equal(["Adam Scott", "Zach Cherry", "Britt Lower"], d.Cast);
        Assert.Equal(["Drama", "Mystery"], d.Genres);
        Assert.Equal((8.7, 520000, "IMDb"), (d.Rating, d.Votes, d.RatingSource));
        Assert.Equal(("Apple TV", "continuing", "2022-02-18", "TV-14", 49), (d.Network, d.Status, d.FirstAired, d.Certification, d.RuntimeMinutes));
        Assert.Equal((2, 40, 5), (d.Seasons, d.Episodes, d.EpisodesDownloaded));
        Assert.Equal(["favourites"], d.Tags);                        // the unknown id 99 is dropped
        Assert.Equal(("imdb-poster.jpg", "fanart.jpg", "United States", "Won 2 Emmys"), (d.PosterUrl, d.BackdropUrl, d.Country, d.Awards));
        Assert.True(d.InSonarr);
        Assert.True(d.HasFullDetails);
    }

    [Fact]
    public async Task Without_omdb_sonarr_and_imdb_fill_in()
    {
        _sonarr.Series.Add(InLibrary());

        var d = (await Create().GetAsync("tt1"))!;

        Assert.Equal("Sonarr overview.", d.Plot);
        Assert.Equal(["Sci-Fi"], d.Genres);
        Assert.Equal(["Adam Scott", "Britt Lower"], d.Cast);        // IMDb's leading cast
        Assert.Equal((8.4, 1000, "Sonarr"), (d.Rating, d.Votes, d.RatingSource));
        Assert.False(d.HasFullDetails);
    }

    [Fact]
    public async Task A_series_outside_the_library_uses_sonarrs_lookup_and_ignores_season_zero()
    {
        _sonarr.LookupResult = JsonNode.Parse("""
            {"title":"Severance","imdbId":"tt1","overview":"From lookup.","network":"Apple TV","genres":["Thriller"],"year":2022,
             "ratings":{"value":8.1,"votes":50},"firstAired":"2022-02-18T00:00:00Z",
             "images":[{"coverType":"fanart","remoteUrl":"lookup-fanart.jpg"}],
             "seasons":[{"seasonNumber":0},{"seasonNumber":1},{"seasonNumber":2}]}
            """)!.AsObject();

        var d = (await Create().GetAsync("tt1"))!;

        Assert.False(d.InSonarr);
        Assert.Equal(("From lookup.", "Apple TV", "lookup-fanart.jpg", 2), (d.Plot, d.Network, d.BackdropUrl, d.Seasons));
        Assert.Empty(d.Tags);
        Assert.Null(d.Episodes);
    }

    [Fact]
    public async Task Sonarrs_certification_wins_over_omdbs_rating_but_omdb_fills_gaps()
    {
        _omdb.Title = FullOmdb();
        var d = (await Create().GetAsync("tt1"))!;                  // not in Sonarr at all
        Assert.Equal(("TV-MA", 50, "18 Feb 2022", "English"), (d.Certification, d.RuntimeMinutes, d.FirstAired, d.Language));
        Assert.Equal(2, d.Seasons);                                  // OMDb's total
    }

    [Fact]
    public async Task Returns_null_when_no_source_knows_the_series()
    {
        _imdb.Title = null;
        Assert.Null(await Create().GetAsync("tt1"));
    }

    [Fact]
    public async Task One_source_being_down_does_not_break_the_page()
    {
        _sonarr.Series.Add(InLibrary());
        _omdb.Fail = new HttpRequestException("omdb down");
        Assert.Equal("Sonarr overview.", (await Create().GetAsync("tt1"))!.Plot);

        _omdb.Fail = null;
        _omdb.Title = FullOmdb();
        _cache.Remove("omdb-title:tt1");
        _sonarr.FailLibrary = new HttpRequestException("sonarr down");
        var d = (await Create().GetAsync("tt1"))!;
        Assert.Equal("The full IMDb plot.", d.Plot);
        Assert.False(d.InSonarr);

        // Even with IMDb and Sonarr both down, the cached OMDb details still render (the title falls back to the id).
        _imdb.Fail = new HttpRequestException("imdb down");
        var onlyOmdb = (await Create().GetAsync("tt1"))!;
        Assert.Equal(("tt1", "The full IMDb plot."), (onlyOmdb.Title, onlyOmdb.Plot));
    }

    [Fact]
    public async Task Omdb_details_are_cached_so_page_views_do_not_use_up_the_quota()
    {
        _omdb.Title = FullOmdb();
        await Create().GetAsync("tt1");
        await Create().GetAsync("tt1");
        Assert.Equal(1, _omdb.Calls);
    }

    [Fact]
    public async Task A_missing_omdb_result_is_not_cached()
    {
        await Create().GetAsync("tt1");
        _omdb.Title = FullOmdb();
        Assert.True((await Create().GetAsync("tt1"))!.HasFullDetails);
        Assert.Equal(2, _omdb.Calls);
    }
}

public class OmdbTitleDetailsTests
{
    static OmdbClient Create(RecordingHandler handler, string key = "k") =>
        new(new StubFactory(handler), new FakeSettings(new AppSettings { OmdbApiKey = key }), new FiltarrOptions());

    const string Full = """
        {"Title":"Severance","Rated":"TV-MA","Released":"18 Feb 2022","Runtime":"49 min","Genre":"Drama, Mystery, Sci-Fi","Actors":"Adam Scott, Britt Lower",
         "Plot":"Mark leads a team.","Language":"English","Country":"United States","Awards":"Won 14 Primetime Emmys","imdbRating":"8.7",
         "imdbVotes":"520,143","totalSeasons":"2","Response":"True"}
        """;

    [Fact]
    public async Task Parses_the_fields_imdb_shows()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Full));
        var t = (await Create(handler).GetTitleAsync("tt11280740"))!;

        Assert.Equal(("Mark leads a team.", "TV-MA", 49, "18 Feb 2022"), (t.Plot, t.Rated, t.RuntimeMinutes, t.Released));
        Assert.Equal(["Adam Scott", "Britt Lower"], t.Actors);
        Assert.Equal(["Drama", "Mystery", "Sci-Fi"], t.Genres);
        Assert.Equal((8.7, 520143, 2), (t.ImdbRating, t.ImdbVotes, t.TotalSeasons));
        Assert.Contains("plot=full", handler.Requests[0].Uri.Query);
        Assert.Contains("i=tt11280740", handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task NA_values_become_nothing()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK,
            """{"Plot":"N/A","Actors":"N/A","Genre":"N/A","Rated":"N/A","Runtime":"N/A","Awards":"N/A","imdbRating":"N/A","imdbVotes":"N/A","totalSeasons":"N/A","Response":"True"}"""));
        var t = (await Create(handler).GetTitleAsync("tt1"))!;

        Assert.Equal((null, 0, 0, null, null, null, null), (t.Plot, t.Actors.Count, t.Genres.Count, t.Rated, t.RuntimeMinutes, t.ImdbRating, t.ImdbVotes));
        Assert.Null(t.TotalSeasons);
    }

    [Fact]
    public async Task Is_best_effort_and_never_throws()
    {
        Assert.Null(await Create(new RecordingHandler((HttpStatusCode.OK, """{"Response":"False","Error":"Incorrect IMDb ID."}"""))).GetTitleAsync("tt1"));
        Assert.Null(await Create(new RecordingHandler((HttpStatusCode.OK, "<html>"))).GetTitleAsync("tt1"));
        Assert.Null(await Create(new RecordingHandler((HttpStatusCode.OK, "{}")) { Throw = new HttpRequestException("down") }).GetTitleAsync("tt1"));
    }

    [Fact]
    public async Task Without_a_key_nothing_is_requested()
    {
        var handler = new RecordingHandler((HttpStatusCode.OK, Full));
        Assert.Null(await Create(handler, key: "").GetTitleAsync("tt1"));
        Assert.Empty(handler.Requests);
    }
}
