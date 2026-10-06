using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Filtarr.Api.Tests;

public class RuleRunTests
{
    readonly FakeSonarr _sonarr = new();
    readonly FakeTracked _tracked = new();
    readonly FakeRuns _runs = new();
    readonly FakeSettings _settings = new();
    readonly FakeRatings _ratings = new();
    readonly SyncGate _gate = new();
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));

    SyncService Create(params Filter[] filters) =>
        new(_sonarr, _settings, new FakeFilters(filters), new FilterEngine(), _ratings, _tracked, _runs, _gate, _clock, NullLogger<SyncService>.Instance);

    static SonarrSeries Show(int id = 1, bool monitored = true) => new(
        id, $"Show{id}", 2020, monitored, "continuing", null, "standard", null, 45, [], null, null, null, $"tt{id}");

    static SonarrEpisode Ep(int id, int number, int seriesId = 1, bool monitored = false, bool hasFile = false, string? finale = null) =>
        new(id, seriesId, $"Ep{number}", 1, number, null, new DateTime(2024, 1, 1), 45, hasFile, monitored, finale);

    static Filter Rule(int id, string name, string field, string op, string value, string? imdb = "tt1",
        FilterAction action = FilterAction.Include, bool enabled = true) => new()
    {
        Id = id, Name = name, ImdbId = imdb, Action = action, Enabled = enabled,
        Conditions = { new() { Field = field, Operator = op, Value = value } },
    };

    static Filter EpisodeNumber(int id, string name, int number, string? imdb = "tt1") => Rule(id, name, "episode.number", "eq", number.ToString(), imdb);

    void Library(params SonarrEpisode[] episodes)
    {
        _sonarr.Series.Add(Show());
        _sonarr.Episodes[1] = episodes.ToList();
    }

    [Fact]
    public async Task Only_the_chosen_rule_is_executed()
    {
        Library(Ep(11, 1), Ep(12, 2), Ep(13, 3));
        var result = await Create(EpisodeNumber(1, "first", 1), EpisodeNumber(2, "second", 2)).RunRuleAsync(1);

        Assert.Equal([11], _sonarr.Monitored);               // rule "second" (episode 2) did not run
        Assert.Equal([11], _sonarr.Searched);
        Assert.Equal((1, 1, 0), (result.Matched, result.Monitored, result.SkippedPreviouslyMonitored));
        Assert.Equal([11], result.NewlyMonitored!.Select(m => m.EpisodeId));
        Assert.Equal(("first", "Show1"), (result.RuleName, result.SeriesTitle));
    }

    [Fact]
    public async Task Other_series_and_global_include_rules_are_not_touched()
    {
        Library(Ep(11, 1));
        _sonarr.Series.Add(Show(2));
        _sonarr.Episodes[2] = [Ep(21, 1, seriesId: 2)];
        var global = EpisodeNumber(5, "global", 1, imdb: null);
        var otherSeries = EpisodeNumber(6, "other series", 1, imdb: "tt2");

        await Create(EpisodeNumber(1, "mine", 1), global, otherSeries).RunRuleAsync(1);

        Assert.Equal([11], _sonarr.Monitored);
    }

    [Fact]
    public async Task Records_the_episodes_in_the_history_and_a_sync_run_named_after_the_rule()
    {
        Library(Ep(11, 1));
        await Create(EpisodeNumber(1, "first", 1)).RunRuleAsync(1);

        Assert.Equal([11], _tracked.Monitored.Keys);
        var run = Assert.Single(_runs.Saved);
        Assert.Equal(("rule: first", 1, 1, 1, null), (run.Trigger, run.SeriesScanned, run.EpisodesMatched, run.EpisodesMonitored, run.Error));
        Assert.Equal("first", run.Items.Single().Filter);
    }

    [Fact]
    public async Task Search_is_only_started_for_episodes_without_a_file_and_respects_the_setting()
    {
        Library(Ep(11, 1), Ep(12, 2, hasFile: true));
        var rule = Rule(1, "all", "episode.season", "gte", "1");
        await Create(rule).RunRuleAsync(1);
        Assert.Equal([11, 12], _sonarr.Monitored.Order());
        Assert.Equal([11], _sonarr.Searched);

        _sonarr.Monitored.Clear(); _sonarr.Searched.Clear(); _tracked.Monitored.Clear();
        _settings.Current = new AppSettings { SearchOnMonitor = false };
        await Create(rule).RunRuleAsync(1);
        Assert.Empty(_sonarr.Searched);
    }

    [Fact]
    public async Task Enabled_never_monitor_filters_still_hold_episodes_back_but_disabled_ones_do_not()
    {
        Library(Ep(11, 1), Ep(12, 2, finale: "season"));
        var include = Rule(1, "all", "episode.season", "gte", "1");
        var noFinales = Rule(2, "no finales", "episode.isFinale", "eq", "true", action: FilterAction.Exclude);

        await Create(include, noFinales).RunRuleAsync(1);
        Assert.Equal([11], _sonarr.Monitored);

        _sonarr.Monitored.Clear(); _tracked.Monitored.Clear();
        noFinales.Enabled = false;
        await Create(include, noFinales).RunRuleAsync(1);
        Assert.Equal([11, 12], _sonarr.Monitored.Order());
    }

    [Fact]
    public async Task Episodes_monitored_before_are_skipped_and_already_monitored_ones_are_counted_separately()
    {
        Library(Ep(11, 1), Ep(12, 2), Ep(13, 3, monitored: true));
        _tracked.Monitored[11] = new DateTime(2024, 1, 1);

        var result = await Create(Rule(1, "all", "episode.season", "gte", "1")).RunRuleAsync(1);

        Assert.Equal([12], _sonarr.Monitored);
        Assert.Equal((3, 1, 1, 1), (result.Matched, result.Monitored, result.SkippedPreviouslyMonitored, result.AlreadyMonitored));
    }

    [Fact]
    public async Task Rules_that_cannot_be_run_are_refused_with_a_reason()
    {
        Library(Ep(11, 1));
        var svc = Create(
            Rule(2, "disabled", "episode.number", "eq", "1", enabled: false),
            Rule(3, "excluder", "episode.number", "eq", "1", action: FilterAction.Exclude),
            Rule(4, "global", "episode.number", "eq", "1", imdb: null));

        var missing = await Assert.ThrowsAsync<RuleRunException>(() => svc.RunRuleAsync(99));
        Assert.Equal(404, missing.StatusCode);
        foreach (var id in new[] { 2, 3, 4 })
            Assert.Equal(400, (await Assert.ThrowsAsync<RuleRunException>(() => svc.RunRuleAsync(id))).StatusCode);

        Assert.Empty(_sonarr.Monitored);
        Assert.Empty(_runs.Saved);
        // A refusal must not leave the gate locked.
        Assert.True(_gate.TryEnter());
    }

    [Fact]
    public async Task A_series_that_is_not_in_sonarr_runs_nothing_and_records_nothing()
    {
        var result = await Create(EpisodeNumber(1, "first", 1)).RunRuleAsync(1);
        Assert.False(result.InSonarr);
        Assert.Empty(_sonarr.Monitored);
        Assert.Empty(_runs.Saved);
    }

    [Fact]
    public async Task An_unmonitored_series_is_skipped_only_while_the_setting_says_so()
    {
        _sonarr.Series.Add(Show(monitored: false));
        _sonarr.Episodes[1] = [Ep(11, 1)];

        var skipped = await Create(EpisodeNumber(1, "first", 1)).RunRuleAsync(1);
        Assert.True(skipped.SkippedBecauseSeriesUnmonitored);
        Assert.Empty(_sonarr.Monitored);

        _settings.Current = new AppSettings { SkipUnmonitoredSeries = false };
        await Create(EpisodeNumber(1, "first", 1)).RunRuleAsync(1);
        Assert.Equal([11], _sonarr.Monitored);
    }

    [Fact]
    public async Task It_cannot_run_while_a_sync_is_running()
    {
        Library(Ep(11, 1));
        Assert.True(_gate.TryEnter());
        await Assert.ThrowsAsync<SyncInProgressException>(() => Create(EpisodeNumber(1, "first", 1)).RunRuleAsync(1));
        Assert.Empty(_sonarr.Monitored);
    }

    [Fact]
    public async Task A_failure_part_way_reports_the_error_keeps_finished_batches_tracked_and_still_records_the_run()
    {
        Library(Enumerable.Range(1, 150).Select(i => Ep(1000 + i, i)).ToArray());
        _sonarr.FailMonitorOnCall = 2;
        _sonarr.FailMonitorWith = new HttpRequestException("boom");

        var result = await Create(Rule(1, "all", "episode.season", "gte", "1")).RunRuleAsync(1);

        Assert.Equal("boom", result.Error);
        Assert.Equal(100, result.Monitored);
        Assert.Equal(100, result.NewlyMonitored!.Count);
        Assert.Equal(100, _tracked.Monitored.Count);
        Assert.Equal("boom", Assert.Single(_runs.Saved).Error);
        Assert.True(_gate.TryEnter()); // released
    }

    [Fact]
    public async Task A_rating_rule_loads_ratings_and_uses_them()
    {
        Library(Ep(11, 1), Ep(12, 2));
        _ratings.Data[(1, 1)] = new("tt11", 9.0);
        _ratings.Data[(1, 2)] = new("tt12", 6.0);

        var result = await Create(Rule(1, "great", "episode.rating", "gte", "8")).RunRuleAsync(1);

        Assert.Equal([11], _sonarr.Monitored);
        Assert.Equal(9.0, result.NewlyMonitored!.Single().Rating);
    }
}
