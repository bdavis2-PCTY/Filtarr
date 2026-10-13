using Filtarr.Core.Models;

namespace Filtarr.Core.Filtering;

/// <summary>The condition fields a filter can use; the keys are what <see cref="FilterEngine.BuildContext"/> provides values for.</summary>
public static class FilterFields
{
    public static readonly FieldDef[] All =
    [
        new("series.rating", "Series rating", "number", "Series"),
        new("series.votes", "Series votes", "number", "Series"),
        new("series.year", "Series year", "number", "Series"),
        new("series.genres", "Series genres", "list", "Series"),
        new("series.network", "Network", "string", "Series"),
        new("series.status", "Series status (continuing/ended)", "string", "Series"),
        new("series.type", "Series type (standard/anime/daily)", "string", "Series"),
        new("series.certification", "Certification (e.g. TV-MA)", "string", "Series"),
        new("series.language", "Original language", "string", "Series"),
        new("episode.season", "Season number", "number", "Episode"),
        new("episode.number", "Episode number", "number", "Episode"),
        new("episode.title", "Episode title", "string", "Episode"),
        new("episode.overview", "Episode overview", "string", "Episode"),
        new("episode.airDate", "Air date", "date", "Episode"),
        new("episode.daysSinceAired", "Days since aired (negative = future)", "number", "Episode"),
        new("episode.runtime", "Runtime (minutes)", "number", "Episode"),
        new("episode.hasFile", "Already downloaded", "bool", "Episode"),
        new("episode.isFinale", "Is a season/series finale", "bool", "Episode"),
        new("episode.rating", "Episode rating (IMDb, 0-10; needs OMDb key)", "number", "Episode"),
        new("episode.isSpecial", "Is a special (season 0)", "bool", "Episode"),
    ];
}
