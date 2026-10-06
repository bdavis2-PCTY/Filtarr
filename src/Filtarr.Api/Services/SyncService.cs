using Filtarr.Api.Models;

namespace Filtarr.Api.Services;

public record MatchedEpisode(int EpisodeId, int SeriesId, string Series, int Season, int Number, string? Title,
    DateTime? AirDate, bool Monitored, bool HasFile, string Filter, string? EpisodeImdbId = null, double? Rating = null,
    string? SeriesImdbId = null, DateTime? PreviouslyMonitoredAt = null);

public record PreviewResult(int SeriesScanned, List<MatchedEpisode> Matches);

/// <summary>What a set of filters would do to one series in Sonarr.</summary>
public record SeriesTestResult(
    bool InSonarr, string? SonarrTitle, bool SeriesMonitored, bool SkippedBecauseUnmonitored,
    int TotalEpisodes, List<MatchedEpisode> Matches, string? Warning = null);

/// <summary>Process-wide lock so only one sync runs at a time (the services themselves are scoped).</summary>
public class SyncGate
{
    readonly SemaphoreSlim _sem = new(1, 1);
    public bool TryEnter() => _sem.Wait(0);
    public void Exit() => _sem.Release();
}

public class SyncService(
    ISonarrClient sonarr, ISettingsService settings, IFilterService filters, IFilterEngine engine,
    IEpisodeRatingService ratings, IMonitoredEpisodeStore tracked, ISyncRunStore runs, SyncGate gate, TimeProvider clock, ILogger<SyncService> log) : ISyncService
{
    static bool SameSeries(Filter f, SonarrSeries s) =>
        f.ImdbId is not null && string.Equals(f.ImdbId, s.ImdbId, StringComparison.OrdinalIgnoreCase);

    public async Task<PreviewResult> PreviewAsync(int? onlySeriesId, CancellationToken ct = default)
    {
        var cfg = await settings.GetAsync(ct);
        var enabled = (await filters.ListAsync(null, false, ct)).Where(f => f.Enabled).ToList();
        var global = enabled.Where(f => f.ImdbId is null).ToList();

        var matches = new List<MatchedEpisode>();
        var scanned = 0;

        foreach (var s in await sonarr.GetSeriesAsync(ct))
        {
            if (onlySeriesId is { } only && s.Id != only) continue;
            if (cfg.SkipUnmonitoredSeries && !s.Monitored) continue;
            var applicable = global.Concat(enabled.Where(f => SameSeries(f, s))).ToList();
            if (!applicable.Any(f => f.Action == FilterAction.Include)) continue;

            scanned++;
            var (found, warning) = await EvaluateAsync(s, applicable, ct);
            matches.AddRange(found);
            if (warning is not null) log.LogWarning("{Series}: {Warning}", s.Title, warning);
        }
        return new(scanned, await MarkPreviouslyMonitoredAsync(matches, ct));
    }

    public async Task<SeriesTestResult> TestSeriesAsync(string imdbId, IReadOnlyList<Filter>? only, CancellationToken ct = default)
    {
        var series = (await sonarr.GetSeriesAsync(ct)).FirstOrDefault(s => string.Equals(s.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
        if (series is null) return new(false, null, false, false, 0, []);

        var cfg = await settings.GetAsync(ct);
        List<Filter> applicable;
        if (only is not null) applicable = only.ToList();
        else
        {
            var enabled = (await filters.ListAsync(null, false, ct)).Where(f => f.Enabled);
            applicable = enabled.Where(f => f.ImdbId is null || SameSeries(f, series)).ToList();
        }

        var episodes = await sonarr.GetEpisodesAsync(series.Id, ct);
        var (matches, warning) = await EvaluateAsync(series, applicable, ct, episodes, fetchRatingsAlways: true);
        matches = await MarkPreviouslyMonitoredAsync(matches, ct);
        return new(true, series.Title, series.Monitored, cfg.SkipUnmonitoredSeries && !series.Monitored, episodes.Count, matches, warning);
    }

    /// <summary>Flags matches that Filtarr monitored in an earlier run; those are never monitored again.</summary>
    async Task<List<MatchedEpisode>> MarkPreviouslyMonitoredAsync(List<MatchedEpisode> matches, CancellationToken ct)
    {
        if (matches.Count == 0) return matches;
        var before = await tracked.GetMonitoredAtAsync(matches.Select(m => m.EpisodeId), ct);
        return matches.Select(m => before.TryGetValue(m.EpisodeId, out var at) ? m with { PreviouslyMonitoredAt = at } : m).ToList();
    }

    /// <summary>An episode matches if any Include filter matches and no Exclude filter does. Callers decide which filters are passed in (e.g. only enabled ones).</summary>
    async Task<(List<MatchedEpisode> Matches, string? Warning)> EvaluateAsync(
        SonarrSeries s, List<Filter> applicable, CancellationToken ct, List<SonarrEpisode>? episodes = null, bool fetchRatingsAlways = false)
    {
        var includes = applicable.Where(f => f.Action == FilterAction.Include).ToList();
        var excludes = applicable.Where(f => f.Action == FilterAction.Exclude).ToList();
        var result = new List<MatchedEpisode>();
        if (includes.Count == 0) return (result, null);

        episodes ??= await sonarr.GetEpisodesAsync(s.Id, ct);
        var (episodeRatings, warning) = await LoadRatingsAsync(s, applicable, episodes, fetchRatingsAlways, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var e in episodes)
        {
            EpisodeRating? rating = null;
            episodeRatings?.TryGetValue((e.SeasonNumber, e.EpisodeNumber), out rating);
            var ctx = engine.BuildContext(s, e, now, rating?.Rating);
            var hit = includes.FirstOrDefault(f => engine.Matches(f, ctx));
            if (hit is null || excludes.Any(f => engine.Matches(f, ctx))) continue;
            result.Add(new(e.Id, s.Id, s.Title, e.SeasonNumber, e.EpisodeNumber, e.Title, e.AirDateUtc, e.Monitored, e.HasFile, hit.Name,
                rating?.ImdbId, rating?.Rating, s.ImdbId));
        }
        return (result, warning);
    }

    /// <summary>
    /// Ratings are only fetched when a filter uses them (or the caller wants episode links, as tests do), to spare the OMDb quota.
    /// A failure never aborts evaluation: rating conditions simply match nothing, and a warning explains why.
    /// </summary>
    async Task<(Dictionary<(int Season, int Episode), EpisodeRating>? Ratings, string? Warning)> LoadRatingsAsync(
        SonarrSeries s, List<Filter> applicable, List<SonarrEpisode> episodes, bool always, CancellationToken ct)
    {
        var needed = applicable.Any(f => f.Conditions.Any(c => c.Field == "episode.rating"));
        if (!needed && !always) return (null, null);
        if (!await ratings.IsConfiguredAsync(ct))
            return (null, needed ? "Episode rating filters need an OMDb API key (set it in Settings); until then they match nothing." : null);
        if (s.ImdbId is null)
            return (null, needed ? "Sonarr has no IMDb id for this series, so episode ratings are unavailable." : null);
        try
        {
            return (await ratings.GetAsync(s.ImdbId, episodes.Select(e => e.SeasonNumber), ct), null);
        }
        catch (Exception ex) when (ex is OmdbException or HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Could not load episode ratings for {Series}", s.Title);
            return (null, $"Could not load episode ratings: {ex.Message}");
        }
    }

    /// <summary>
    /// Monitors the episodes in Sonarr batch by batch (starting a search for those without a file when enabled) and records
    /// them in the history. Each batch is recorded as soon as Sonarr has accepted it, so a failure part-way never leaves
    /// monitored episodes untracked; <paramref name="run"/> counts what was done so far.
    /// </summary>
    async Task ApplyAsync(List<MatchedEpisode> todo, AppSettings cfg, SyncRun run, CancellationToken ct)
    {
        foreach (var batch in todo.Chunk(100))
        {
            await sonarr.MonitorAsync(batch.Select(m => m.EpisodeId), ct);
            var now = clock.GetUtcNow().UtcDateTime;
            await tracked.RecordAsync(batch.Select(m => new MonitoredEpisode
            {
                EpisodeId = m.EpisodeId, SeriesId = m.SeriesId, SeriesImdbId = m.SeriesImdbId, SeriesTitle = m.Series,
                Season = m.Season, Number = m.Number, EpisodeTitle = m.Title, Filter = m.Filter, MonitoredAt = now,
            }), CancellationToken.None);
            run.EpisodesMonitored += batch.Length;
            run.Items.AddRange(batch.Take(Math.Max(0, 500 - run.Items.Count)).Select(m => new SyncedItem
            {
                EpisodeId = m.EpisodeId, Series = m.Series, Episode = $"S{m.Season:00}E{m.Number:00} {m.Title}", Filter = m.Filter,
            }));
            if (cfg.SearchOnMonitor)
            {
                var missing = batch.Where(m => !m.HasFile).Select(m => m.EpisodeId).ToList();
                if (missing.Count > 0) await sonarr.SearchAsync(missing, ct);
            }
        }
    }

    public async Task<RuleRunResult> RunRuleAsync(int filterId, CancellationToken ct = default)
    {
        if (!gate.TryEnter()) throw new SyncInProgressException();
        var run = new SyncRun { StartedAt = clock.GetUtcNow().UtcDateTime, Trigger = "rule" };
        var record = false;
        var result = new RuleRunResult();
        try
        {
            var all = await filters.ListAsync(null, false, ct);
            var rule = all.FirstOrDefault(f => f.Id == filterId) ?? throw new RuleRunException("That filter no longer exists.", 404);
            if (rule.ImdbId is null) throw new RuleRunException("Only a series' own filters can be run on their own; global filters run with the sync.", 400);
            if (!rule.Enabled) throw new RuleRunException("This filter is disabled. Enable it to run it.", 400);
            if (rule.Action != FilterAction.Include)
                throw new RuleRunException("A \"never monitor\" filter doesn't monitor anything by itself; it only holds back other filters.", 400);
            result = result with { RuleName = rule.Name };
            run.Trigger = $"rule: {rule.Name}";

            var series = (await sonarr.GetSeriesAsync(ct)).FirstOrDefault(s => string.Equals(s.ImdbId, rule.ImdbId, StringComparison.OrdinalIgnoreCase));
            if (series is null) return result with { InSonarr = false };
            result = result with { InSonarr = true, SeriesTitle = series.Title };

            var cfg = await settings.GetAsync(ct);
            if (cfg.SkipUnmonitoredSeries && !series.Monitored) return result with { SkippedBecauseSeriesUnmonitored = true };

            record = true;
            run.SeriesScanned = 1;
            try
            {
                // Only this rule decides what to monitor. Enabled "never monitor" filters still hold episodes back.
                var guards = all.Where(f => f.Enabled && f.Action == FilterAction.Exclude && (f.ImdbId is null || SameSeries(f, series)));
                var (found, warning) = await EvaluateAsync(series, [rule, .. guards], ct);
                var matches = await MarkPreviouslyMonitoredAsync(found, ct);
                var notMonitored = matches.Where(m => !m.Monitored).ToList();
                var todo = notMonitored.Where(m => m.PreviouslyMonitoredAt is null).ToList();
                run.EpisodesMatched = matches.Count;
                run.EpisodesSkipped = notMonitored.Count - todo.Count;
                result = result with
                {
                    Matched = matches.Count, AlreadyMonitored = matches.Count - notMonitored.Count,
                    SkippedPreviouslyMonitored = run.EpisodesSkipped, Warning = warning,
                };
                try { await ApplyAsync(todo, cfg, run, ct); }
                finally { result = result with { Monitored = run.EpisodesMonitored, NewlyMonitored = todo.Take(run.EpisodesMonitored).ToList() }; }
            }
            catch (Exception ex) when (ex is not RuleRunException)
            {
                log.LogError(ex, "Running rule {Rule} failed", rule.Name);
                run.Error = ex.Message;
                result = result with { Error = ex.Message };
            }
            return result;
        }
        finally
        {
            run.FinishedAt = clock.GetUtcNow().UtcDateTime;
            try { if (record) await runs.AddAsync(run, CancellationToken.None); }
            finally { gate.Exit(); }
        }
    }

    public async Task<SyncRun> RunAsync(string trigger, CancellationToken ct = default)
    {
        if (!gate.TryEnter()) throw new SyncInProgressException();
        var run = new SyncRun { StartedAt = clock.GetUtcNow().UtcDateTime, Trigger = trigger };
        try
        {
            var cfg = await settings.GetAsync(ct);
            var preview = await PreviewAsync(null, ct);
            run.SeriesScanned = preview.SeriesScanned;
            run.EpisodesMatched = preview.Matches.Count;

            // Episodes Filtarr monitored before are left alone even if they are unmonitored in Sonarr again (e.g. watched and deleted).
            var notMonitored = preview.Matches.Where(m => !m.Monitored).ToList();
            var todo = notMonitored.Where(m => m.PreviouslyMonitoredAt is null).ToList();
            run.EpisodesSkipped = notMonitored.Count - todo.Count;

            await ApplyAsync(todo, cfg, run, ct);
            if (run.EpisodesSkipped > 0)
                log.LogInformation("{Count} matched episode(s) were skipped because Filtarr already monitored them before.", run.EpisodesSkipped);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sync failed");
            run.Error = ex.Message;
        }
        finally
        {
            run.FinishedAt = clock.GetUtcNow().UtcDateTime;
            try { await runs.AddAsync(run, CancellationToken.None); }
            finally { gate.Exit(); }
        }
        return run;
    }
}

public class SyncWorker(IServiceScopeFactory scopes, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        DateTime? last = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var s = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync(ct);
                var now = clock.GetUtcNow().UtcDateTime;
                var due = last is null || now - last >= TimeSpan.FromMinutes(Math.Max(1, s.SyncIntervalMinutes));
                if (s.AutoSyncEnabled && due)
                {
                    last = now;
                    await scope.ServiceProvider.GetRequiredService<ISyncService>().RunAsync("auto", ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* RunAsync records its own failures; settings read errors retry next tick */ }
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith(_ => { });
        }
    }
}
