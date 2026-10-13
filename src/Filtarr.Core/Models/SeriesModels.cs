namespace Filtarr.Core.Models;

public record SeriesInfo(
    string ImdbId, string Title, int? Year, string? YearRange, string? Cast, string? ImageUrl,
    bool InSonarr, int? SonarrId, bool? SonarrMonitored, int FilterCount = 0, int DisabledFilterCount = 0,
    /// <summary>Address of the series page in Sonarr; null when the series isn't in the library.</summary>
    string? SonarrLink = null);

/// <summary>Everything the series page shows, merged from IMDb, OMDb and Sonarr. Any part can be missing.</summary>
public record SeriesDetails(
    string ImdbId, string Title, int? Year, string? YearRange, string? PosterUrl, string? BackdropUrl,
    string? Plot, List<string> Cast, List<string> Genres,
    string? Certification, int? RuntimeMinutes, string? Network, string? Status, string? FirstAired,
    string? Language, string? Country, string? Awards,
    double? Rating, int? Votes, string? RatingSource,
    int? Seasons, int? Episodes, int? EpisodesDownloaded,
    List<string> Tags, bool InSonarr, bool HasFullDetails);

/// <summary>Filter counts for one series. <see cref="SeriesTitle"/> is the title stored when its filters were created.</summary>
public record SeriesFilterSummary(string ImdbId, string? SeriesTitle, int FilterCount, int DisabledCount);

/// <summary>A series just added to Sonarr.</summary>
public record AddedSeries(int SonarrId, string Title, string? ImdbId, string? TitleSlug);

/// <summary>A series in the Sonarr library, as listed on the filters page.</summary>
public record SonarrSeriesSummary(int Id, string Title, int Year, bool Monitored);

/// <summary>A condition field a filter can use (the filter editor's dropdown).</summary>
public record FieldDef(string Key, string Label, string Type, string Group);

/// <summary>Validation of IMDb ids as they appear in URLs and API calls.</summary>
public static partial class ImdbId
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^tt\d{1,12}$")]
    private static partial System.Text.RegularExpressions.Regex Pattern();

    /// <summary>True for ids like "tt0903747".</summary>
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);
}
