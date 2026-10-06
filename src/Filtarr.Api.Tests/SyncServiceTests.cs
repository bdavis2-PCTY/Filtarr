using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

public class SyncServiceTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeRuns _runs = new();
    readonly FakeSettings _settings = new();
    readonly SyncGate _gate = new();
    readonly FakeRatings _ratings = new();
    readonly FakeTracked _tracked = new();

    SyncService Create(params Filter[] filters) =>
        new(_sonarr, _settings, new FakeFilters(filters), new FilterEngine(), _ratings, _tracked, _runs, _gate,
            new FakeTimeProvider(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)), NullLogger<SyncService>.Instance);

    static SonarrSeries Show(int id, double rating, bool monitored = true) => new(
        id, $"Show{id}", 2020, monitored, "continuing", null, "standard", null, 45, [], new SonarrRatings(rating, 100), null, null, $"tt{id}");

    static SonarrEpisode Ep(int id, int seriesId, int number, bool monitored = false, bool hasFile = false, string? finale = null) =>
        new(id, seriesId, $"Ep{number}", 1, number, null, new DateTime(2024, 1, 1), 45, hasFile, monitored, finale);

    static Filter Rating(double min, FilterAction a = FilterAction.Include, string? imdbId = null) => new()
    {
        Name = $"rating>={min}", Action = a, ImdbId = imdbId,
        Conditions = { new() { Field = "series.rating", Operator = "gte", Value = min.ToString() } },
    };

    [Fact]
    public async Task Monitors_only_unmonitored_matches_and_searches_only_missing_files()
    {
        _sonarr.Series.Add(Show(1, 9));
        _sonarr.Episodes[1] = [Ep(11, 1, 1), Ep(12, 1, 2, monitored: true), Ep(13, 1, 3, hasFile: true)];

        var run = await Create(Rating(8)).RunAsync("test");

        Assert.Null(run.Error);
        Assert.Equal(3, run.EpisodesMatched);
        Assert.Equal([11, 13], _sonarr.Monitored.Order());
        Assert.Equal([11], _sonarr.Searched);
        Assert.Single(_runs.Saved);
    }

    [Fact]
    public async Task Search_can_be_disabled()
    {
        _settings.Current = new AppSettings { SearchOnMonitor = false };
        _sonarr.Series.Add(Show(1, 9));
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];
        await Create(Rating(8)).RunAsync("test");
        Assert.Empty(_sonarr.Searched);
    }

    [Fact]
    public async Task Exclude_filter_wins_over_include()
    {
        _sonarr.Series.Add(Show(1, 9));
        _sonarr.Episodes[1] = [Ep(11, 1, 1), Ep(12, 1, 2, finale: "season")];
        var noFinales = new Filter
        {
            Name = "no finales", Action = FilterAction.Exclude,
            Conditions = { new() { Field = "episode.isFinale", Operator = "eq", Value = "true" } },
        };

        var preview = await Create(Rating(8), noFinales).PreviewAsync(null);

        Assert.Equal([11], preview.Matches.Select(m => m.EpisodeId));
    }

    [Fact]
    public async Task Series_filters_apply_only_to_their_series_and_disabled_filters_are_ignored()
    {
        _sonarr.Series.AddRange([Show(1, 1), Show(2, 1)]);
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];
        _sonarr.Episodes[2] = [Ep(21, 2, 1)];
        var disabled = Rating(0);
        disabled.Enabled = false;

        var preview = await Create(Rating(0, imdbId: "tt2"), disabled).PreviewAsync(null);

        Assert.Equal([21], preview.Matches.Select(m => m.EpisodeId));
        Assert.Equal(1, preview.SeriesScanned);
    }

    [Fact]
    public async Task Unmonitored_series_are_skipped_by_default_and_included_when_configured()
    {
        _sonarr.Series.Add(Show(1, 9, monitored: false));
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];
        Assert.Empty((await Create(Rating(8)).PreviewAsync(null)).Matches);

        _settings.Current = new AppSettings { SkipUnmonitoredSeries = false };
        Assert.Single((await Create(Rating(8)).PreviewAsync(null)).Matches);
    }

    [Fact]
    public async Task Preview_can_be_limited_to_one_series()
    {
        _sonarr.Series.AddRange([Show(1, 9), Show(2, 9)]);
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];
        _sonarr.Episodes[2] = [Ep(21, 2, 1)];
        var preview = await Create(Rating(8)).PreviewAsync(2);
        Assert.Equal([21], preview.Matches.Select(m => m.EpisodeId));
    }

    [Fact]
    public async Task Failures_are_recorded_and_the_gate_is_released()
    {
        _sonarr.Series.Add(Show(1, 9));
        _sonarr.Episodes[1] = [Ep(11, 1, 1)];
        _sonarr.FailMonitorWith = new HttpRequestException("boom");
        var svc = Create(Rating(8));

        var run = await svc.RunAsync("test");

        Assert.Equal("boom", run.Error);
        Assert.Single(_runs.Saved);
        _sonarr.FailMonitorWith = null;
        Assert.Null((await svc.RunAsync("test")).Error);
    }

    [Fact]
    public async Task Concurrent_runs_are_rejected()
    {
        Assert.True(_gate.TryEnter());
        await Assert.ThrowsAsync<SyncInProgressException>(() => Create(Rating(8)).RunAsync("test"));
    }
}
