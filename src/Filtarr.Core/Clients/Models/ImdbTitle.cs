namespace Filtarr.Core.Clients.Models;

public record ImdbTitle(string ImdbId, string Title, int? Year, string? YearRange, string? Cast, string? ImageUrl, string Type);
