namespace Filtarr.Core.Configuration;

/// <summary>Where the external services live. Bound from the "Filtarr" section of appsettings.json (e.g. Filtarr__OmdbUrl).</summary>
public class ExternalApiOptions
{
    /// <summary>Base address of the OMDb API (the key is set on the Settings page).</summary>
    public string OmdbUrl { get; set; } = "https://www.omdbapi.com/";

    /// <summary>Base URL of IMDb's search-suggestion endpoint used by the series search.</summary>
    public string ImdbSuggestionUrl { get; set; } = "https://v3.sg.media-imdb.com/suggestion";
}
