using System.Text.Json.Serialization;

namespace Filtarr.Api.Services;

public record SonarrSeries(
    int Id, string Title, int Year, bool Monitored, string? Status, string? Network, string? SeriesType,
    string? Certification, int Runtime, List<string>? Genres, SonarrRatings? Ratings,
    SonarrLanguage? OriginalLanguage, List<SonarrImage>? Images, string? ImdbId = null, string? TitleSlug = null,
    string? Overview = null, List<int>? Tags = null, SonarrStatistics? Statistics = null, string? FirstAired = null);
public record SonarrStatistics(int SeasonCount, int EpisodeCount, int EpisodeFileCount, int TotalEpisodeCount);
public record SonarrTag(int Id, string Label);
public record SonarrRootFolder(string Path, long? FreeSpace);
public record SonarrQualityProfile(int Id, string Name);
public record SonarrRatings(double Value, int Votes);
public record SonarrLanguage(string? Name);
public record SonarrImage(string CoverType, string? RemoteUrl, string? Url);
public record SonarrEpisode(
    int Id, int SeriesId, string? Title, int SeasonNumber, int EpisodeNumber, string? Overview,
    DateTime? AirDateUtc, int Runtime, bool HasFile, bool Monitored, string? FinaleType);

public record FieldDef(string Key, string Label, string Type, string Group);

public static class Fields
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
