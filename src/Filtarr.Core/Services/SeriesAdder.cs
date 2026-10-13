using System.Text.Json.Nodes;
using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Clients.Models;
using Filtarr.Core.Exceptions;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Core.Services;

public class SeriesAdder(ISonarrClient sonarr, ISettingsService settings) : ISeriesAdder
{
    public async Task<AddedSeries> AddAsync(string imdbId, CancellationToken ct = default)
    {
        var library = await sonarr.GetSeriesAsync(ct);
        if (library.Any(s => string.Equals(s.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)))
            throw new SeriesAddException("This series is already in Sonarr.", 409);

        var lookup = await sonarr.LookupSeriesAsync(imdbId, ct)
            ?? throw new SeriesAddException($"Sonarr could not find {imdbId} in its metadata source, so it can't be added.", 404);

        var body = (JsonObject)lookup.DeepClone();
        var chosen = await settings.GetEffectiveAsync(ct);
        body["qualityProfileId"] = (await PickQualityProfileAsync(chosen.SonarrQualityProfile, ct)).Id;
        body["rootFolderPath"] = await PickRootFolderAsync(chosen.SonarrRootFolderPath, ct);
        body["monitored"] = true;          // the series itself is monitored (so Filtarr syncs don't skip it)...
        body["seasonFolder"] = true;
        body["monitorNewItems"] = "none";  // ...but nothing under it is, now or when new seasons appear
        if (body["seasons"] is JsonArray seasons)
            foreach (var season in seasons.OfType<JsonObject>()) season["monitored"] = false;
        body["addOptions"] = new JsonObject
        {
            ["monitor"] = "none",
            ["searchForMissingEpisodes"] = false,
            ["searchForCutoffUnmetEpisodes"] = false,
        };

        try
        {
            var added = await sonarr.AddSeriesAsync(body, ct);
            return new AddedSeries(added.Id, added.Title, added.ImdbId, added.TitleSlug);
        }
        catch (SonarrException ex) { throw new SeriesAddException(ex.Message, ex.StatusCode == 400 ? 400 : 502); }
    }

    async Task<string> PickRootFolderAsync(string configured, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        var folders = await sonarr.GetRootFoldersAsync(ct);
        return folders.FirstOrDefault()?.Path
               ?? throw new SeriesAddException("Sonarr has no root folder configured. Add one in Sonarr (Settings > Media Management) or choose one in Filtarr's settings.", 400);
    }

    async Task<SonarrQualityProfile> PickQualityProfileAsync(string configured, CancellationToken ct)
    {
        var profiles = await sonarr.GetQualityProfilesAsync(ct);
        if (string.IsNullOrWhiteSpace(configured))
            return profiles.FirstOrDefault() ?? throw new SeriesAddException("Sonarr has no quality profiles.", 400);

        var wanted = configured.Trim();
        // Compare trimmed: profile names in Sonarr can carry stray whitespace.
        return profiles.FirstOrDefault(p => string.Equals(p.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new SeriesAddException(
                   $"Sonarr has no quality profile named \"{wanted}\" (chosen in Filtarr's settings). Available: {string.Join(", ", profiles.Select(p => p.Name.Trim()))}.", 400);
    }
}
