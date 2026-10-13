namespace Filtarr.Core.Clients.Models;

// Shapes of Sonarr's v3 API resources (only the fields Filtarr reads). These are client types: services translate them into
// Filtarr.Core.Models before anything leaves the library.

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
