namespace Filtarr.Core.Clients.Models;

public record OmdbEpisode(int Number, string? ImdbId, double? Rating);

public record OmdbTitle(
    string? Plot, List<string> Actors, List<string> Genres, string? Rated, int? RuntimeMinutes, string? Released,
    string? Language, string? Country, string? Awards, double? ImdbRating, int? ImdbVotes, int? TotalSeasons);

public record OmdbTestResult(bool Ok, string Message);
