using Filtarr.Api.Models;
using Filtarr.Api.Services;

namespace Filtarr.Api.Tests;

public class FakeSonarr : ISonarrClient
{
    public List<SonarrSeries> Series { get; } = new();
    public Dictionary<int, List<SonarrEpisode>> Episodes { get; } = new();
    public List<int> Monitored { get; } = new();
    public List<int> Searched { get; } = new();
    public Exception? FailMonitorWith { get; set; }
    /// <summary>1-based MonitorAsync call that fails; 0 = every call fails (when FailMonitorWith is set).</summary>
    public int FailMonitorOnCall { get; set; }
    int _monitorCalls;

    public List<(string? Url, string? Key)> Tests { get; } = new();
    public Exception? FailTest { get; set; }
    public Task<string> TestAsync(string? url, string? apiKey, CancellationToken ct = default)
    {
        Tests.Add((url, apiKey));
        return FailTest is not null ? throw FailTest : Task.FromResult("Sonarr 4.0");
    }

    // --- adding series
    public System.Text.Json.Nodes.JsonObject? LookupResult { get; set; }
    public List<SonarrRootFolder> RootFolders { get; } = new();
    public List<SonarrQualityProfile> Profiles { get; } = new();
    public List<SonarrTag> Tags { get; } = new();
    public Task<List<SonarrTag>> GetTagsAsync(CancellationToken ct = default) => Task.FromResult(Tags);
    public List<System.Text.Json.Nodes.JsonObject> Added { get; } = new();
    public SonarrException? FailAdd { get; set; }
    public Task<System.Text.Json.Nodes.JsonObject?> LookupSeriesAsync(string imdbId, CancellationToken ct = default) => Task.FromResult(LookupResult);
    public Task<List<SonarrRootFolder>> GetRootFoldersAsync(CancellationToken ct = default) => Task.FromResult(RootFolders);
    public Task<List<SonarrQualityProfile>> GetQualityProfilesAsync(CancellationToken ct = default) => Task.FromResult(Profiles);
    public Task<SonarrSeries> AddSeriesAsync(System.Text.Json.Nodes.JsonObject series, CancellationToken ct = default)
    {
        if (FailAdd is not null) throw FailAdd;
        Added.Add(series);
        return Task.FromResult(new SonarrSeries(99, (string?)series["title"] ?? "", 2000, true, null, null, null, null, 45, [], null, null, null, (string?)series["imdbId"], (string?)series["titleSlug"]));
    }
    public Exception? FailLibrary { get; set; }
    public Task<List<SonarrSeries>> GetSeriesAsync(CancellationToken ct = default) =>
        FailLibrary is not null ? throw FailLibrary : Task.FromResult(Series);
    public Task<List<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken ct = default) =>
        Task.FromResult(Episodes.GetValueOrDefault(seriesId) ?? []);
    public Task MonitorAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        _monitorCalls++;
        if (FailMonitorWith is not null && (FailMonitorOnCall == 0 || FailMonitorOnCall == _monitorCalls)) throw FailMonitorWith;
        Monitored.AddRange(ids);
        return Task.CompletedTask;
    }
    public Task SearchAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        Searched.AddRange(ids);
        return Task.CompletedTask;
    }
}

public class FakeSettings(AppSettings? s = null) : ISettingsService
{
    public AppSettings Current { get; set; } = s ?? new AppSettings { ApiKey = "k" };
    public SettingsUpdate? LastUpdate { get; private set; }
    public Task<AppSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default) => Task.FromResult(EffectiveSettings.From(Current));
    public Task UpdateAsync(SettingsUpdate u, CancellationToken ct = default)
    {
        LastUpdate = u;
        return Task.CompletedTask;
    }
}

public class FakeFilters(params Filter[] filters) : IFilterService
{
    public Task<List<Filter>> ListAsync(string? imdbId, bool globalOnly, CancellationToken ct = default) => Task.FromResult(filters.ToList());
    public Task<Filter> CreateAsync(Filter f, CancellationToken ct = default) => Task.FromResult(f);
    public Task<Filter?> UpdateAsync(int id, Filter f, CancellationToken ct = default) => Task.FromResult<Filter?>(f);
    public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => Task.FromResult(true);
    public Task<List<SeriesFilterSummary>> SummarizeBySeriesAsync(IEnumerable<string>? imdbIds, CancellationToken ct = default)
    {
        var wanted = imdbIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(filters.Where(f => f.ImdbId is not null && (wanted is null || wanted.Contains(f.ImdbId)))
            .GroupBy(f => f.ImdbId!)
            .Select(g => new SeriesFilterSummary(g.Key, g.Select(f => f.SeriesTitle).FirstOrDefault(t => t is not null), g.Count(), g.Count(f => !f.Enabled)))
            .ToList());
    }
}

public class FakeRatings : IEpisodeRatingService
{
    public bool IsConfigured { get; set; } = true;
    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) => Task.FromResult(IsConfigured);
    public Dictionary<(int, int), EpisodeRating> Data { get; } = new();
    public Exception? Fail { get; set; }
    public int Calls { get; private set; }

    public Task<Dictionary<(int Season, int Episode), EpisodeRating>> GetAsync(string seriesImdbId, IEnumerable<int> seasons, CancellationToken ct = default)
    {
        Calls++;
        if (Fail is not null) throw Fail;
        return Task.FromResult(new Dictionary<(int Season, int Episode), EpisodeRating>(Data));
    }
}

public class FakeTracked : IMonitoredEpisodeStore
{
    public Dictionary<int, DateTime> Monitored { get; } = new();
    public List<List<int>> RecordedBatches { get; } = new();

    public Task<Dictionary<int, DateTime>> GetMonitoredAtAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        Task.FromResult(ids.Where(Monitored.ContainsKey).Distinct().ToDictionary(i => i, i => Monitored[i]));

    public List<SeriesKey> Resets { get; } = new();
    public int CountResult { get; set; }
    public Task<int> CountForSeriesAsync(SeriesKey series, CancellationToken ct = default) => Task.FromResult(CountResult);
    public Task<int> RemoveForSeriesAsync(SeriesKey series, CancellationToken ct = default)
    {
        Resets.Add(series);
        return Task.FromResult(CountResult);
    }

    public Task RecordAsync(IEnumerable<MonitoredEpisode> episodes, CancellationToken ct = default)
    {
        var list = episodes.ToList();
        RecordedBatches.Add(list.Select(e => e.EpisodeId).ToList());
        foreach (var e in list) Monitored.TryAdd(e.EpisodeId, e.MonitoredAt);
        return Task.CompletedTask;
    }
}

public class FakeRuns : ISyncRunStore
{
    public List<SyncRun> Saved { get; } = new();
    public Task AddAsync(SyncRun run, CancellationToken ct = default)
    {
        Saved.Add(run);
        return Task.CompletedTask;
    }
    public Task<List<SyncRun>> RecentAsync(int count, CancellationToken ct = default) => Task.FromResult(Saved.ToList());
}
