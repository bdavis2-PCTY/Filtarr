namespace Filtarr.Data.Models;

public class CachedEpisodeRating
{
    public int Number { get; set; }
    public string? ImdbId { get; set; }
    public double? Rating { get; set; }
}

/// <summary>Cache of one season's IMDb episode ratings (fetched via OMDb) so syncs don't burn through the API quota.</summary>
public class SeasonRatings
{
    public int Id { get; set; }
    public string SeriesImdbId { get; set; } = "";
    public int Season { get; set; }
    public DateTime FetchedAt { get; set; }
    public List<CachedEpisodeRating> Episodes { get; set; } = new();
}
