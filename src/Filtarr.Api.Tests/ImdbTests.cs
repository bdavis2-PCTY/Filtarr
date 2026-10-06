using System.Net;
using Filtarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Filtarr.Api.Tests;

class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
}

class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public class ImdbClientTests
{
    const string Json = """
        {"d":[
          {"id":"tt0903747","l":"Breaking Bad","qid":"tvSeries","y":2008,"yr":"2008-2013","s":"Bryan Cranston","i":{"imageUrl":"http://img/bb.jpg"}},
          {"id":"in0000274","l":"Breaking Bad","s":"Franchise"},
          {"id":"tt9243946","l":"El Camino","qid":"movie","y":2019},
          {"id":"tt2387761","l":"Minisodes","qid":"tvMiniSeries","y":2009}
        ]}
        """;

    static (ImdbClient, StubHandler) Create()
    {
        var handler = new StubHandler(Json);
        return (new ImdbClient(new StubFactory(handler), new FiltarrOptions()), handler);
    }

    [Fact]
    public async Task Search_returns_only_tv_series_and_maps_fields()
    {
        var (client, handler) = Create();
        var results = await client.SearchSeriesAsync("Breaking Bad");

        Assert.Equal(["tt0903747", "tt2387761"], results.Select(r => r.ImdbId));
        Assert.Equal(new ImdbTitle("tt0903747", "Breaking Bad", 2008, "2008-2013", "Bryan Cranston", "http://img/bb.jpg", "tvSeries"), results[0]);
        Assert.EndsWith("/b/Breaking%20Bad.json", handler.Requests.Single().AbsoluteUri);
    }

    [Fact]
    public async Task Short_queries_do_not_call_imdb()
    {
        var (client, handler) = Create();
        Assert.Empty(await client.SearchSeriesAsync(" a "));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Non_alphanumeric_queries_use_the_x_shard()
    {
        var (client, handler) = Create();
        await client.SearchSeriesAsync("-ab");
        Assert.Contains("/x/", handler.Requests.Single().AbsoluteUri);
    }

    [Fact]
    public async Task Get_requires_a_valid_id_and_an_exact_series_match()
    {
        var (client, handler) = Create();
        Assert.Null(await client.GetSeriesAsync("../etc"));
        Assert.Empty(handler.Requests);
        Assert.Null(await client.GetSeriesAsync("tt9243946")); // a movie
        Assert.Equal("Breaking Bad", (await client.GetSeriesAsync("tt0903747"))!.Title);
    }
}

public class SeriesCatalogTests
{
    class FakeImdb(params ImdbTitle[] titles) : IImdbClient
    {
        public bool Fail { get; set; }
        public Task<List<ImdbTitle>> SearchSeriesAsync(string q, CancellationToken ct = default) => Task.FromResult(titles.ToList());
        public Task<ImdbTitle?> GetSeriesAsync(string id, CancellationToken ct = default) =>
            Fail ? throw new HttpRequestException("down") : Task.FromResult(titles.FirstOrDefault(t => t.ImdbId == id));
    }

    static readonly ImdbTitle Bb = new("tt1", "Breaking Bad", 2008, "2008-2013", null, "img", "tvSeries");

    static SonarrSeries InLibrary(string imdb) =>
        new(7, "Breaking Bad", 2008, true, null, null, null, null, 45, [], null, null, [new("poster", "remote.jpg", null)], imdb);

    [Fact]
    public async Task Search_flags_series_already_in_sonarr()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(InLibrary("tt1"));
        var catalog = new SeriesCatalog(new FakeImdb(Bb, Bb with { ImdbId = "tt2" }), sonarr, new FakeFilters(), new FakeSettings(), NullLogger<SeriesCatalog>.Instance);

        var results = await catalog.SearchAsync("bb");

        Assert.True(results[0].InSonarr);
        Assert.Equal(7, results[0].SonarrId);
        Assert.False(results[1].InSonarr);
    }

    [Fact]
    public async Task Get_falls_back_to_sonarr_data_when_imdb_is_down_and_returns_null_when_unknown()
    {
        var sonarr = new FakeSonarr();
        sonarr.Series.Add(InLibrary("tt1"));
        var catalog = new SeriesCatalog(new FakeImdb { Fail = true }, sonarr, new FakeFilters(), new FakeSettings(), NullLogger<SeriesCatalog>.Instance);

        var info = await catalog.GetAsync("tt1");
        Assert.Equal("remote.jpg", info!.ImageUrl);
        Assert.True(info.InSonarr);
        Assert.Null(await catalog.GetAsync("tt999"));
    }
}
