using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

public class SeriesTestTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeSettings _settings = new();
    readonly FakeRatings _ratings = new();
    readonly FakeTracked _tracked = new();

    SyncService Create(params Filter[] filters) =>
        new(_sonarr, _settings, new FakeFilters(filters), new FilterEngine(), _ratings, _tracked, new FakeRuns(), new SyncGate(),
            new FakeTimeProvider(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<SyncService>.Instance);

    static SonarrSeries Show(int id, bool monitored = true) => new(
        id, $"Show{id}", 2020, monitored, "continuing", null, "standard", null, 45, [], new SonarrRatings(9, 100), null, null, $"tt{id}");

    static SonarrEpisode Ep(int id, int seriesId, int number, bool monitored = false, bool hasFile = false) =>
        new(id, seriesId, $"Ep{number}", 1, number, null, new DateTime(2024, 1, 1), 45, hasFile, monitored, null);

    static Filter EpisodeNumber(int n, string? imdbId = null, string name = "n") => new()
    {
        Name = name, ImdbId = imdbId,
        Conditions = { new() { Field = "episode.number", Operator = "eq", Value = n.ToString() } },
    };

    [Fact]
    public async Task Series_not_in_sonarr_reports_InSonarr_false()
    {
        var result = await Create(EpisodeNumber(1)).TestSeriesAsync("tt404", null);
        Assert.False(result.InSonarr);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Saved_filters_use_global_plus_this_series_only()
    {
        _sonarr.Series.AddRange([Show(1), Show(2)]);
        _sonarr.Episodes[1] = [Ep(11, 1, 1), Ep(12, 1, 2), Ep(13, 1, 3)];

        var result = await Create(EpisodeNumber(1, null, "global"), EpisodeNumber(2, "tt1", "mine"), EpisodeNumber(3, "tt2", "other series"))
            .TestSeriesAsync("tt1", null);

        Assert.True(result.InSonarr);
        Assert.Equal(3, result.TotalEpisodes);
        Assert.Equal([11, 12], result.Matches.Select(m => m.EpisodeId).Order());
    }

    [Fact]
    public async Task Explicit_filters_are_evaluated_alone_even_when_they_are_disabled_drafts()
    {
        _sonarr.Series.Add(Show(1));
        _sonarr.Episodes[1] = [Ep(11, 1, 1), Ep(12, 1, 2)];
        var draft = EpisodeNumber(2, "tt1", "draft");
        draft.Enabled = false;

        var result = await Create(EpisodeNumber(1)).TestSeriesAsync("tt1", [draft]);

        Assert.Equal([12], result.Matches.Select(m => m.EpisodeId));
    }

    [Fact]
    public async Task Reports_when_sync_would_skip_the_series_because_it_is_unmonitored()
    {
        _sonarr.Series.Add(Show(1, monitored: false));
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];

        var result = await Create(EpisodeNumber(1)).TestSeriesAsync("tt1", null);

        Assert.True(result.SkippedBecauseUnmonitored);
        Assert.Single(result.Matches);
    }
}
