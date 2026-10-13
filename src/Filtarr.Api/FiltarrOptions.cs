namespace Filtarr.Api;

/// <summary>Bound from the "Filtarr" section of appsettings.json (override with env vars, e.g. Filtarr__Port).</summary>
public class FiltarrOptions
{
    public const string Section = "Filtarr";

    /// <summary>HTTP port the API/UI listens on.</summary>
    public int Port { get; set; } = 5080;

    // The Sonarr URL / API key / root folder / quality profile and the OMDb API key are not here: they are edited on the
    // Settings page and stored in the database (see AppSettings).

    /// <summary>Directory for everything Filtarr writes (database, etc.). Environment variables such as %APPDATA% are expanded.</summary>
    public string DataDirectory { get; set; } = "%APPDATA%/Filtarr";

    /// <summary>
    /// Directory for log files. Empty means "Logs" inside the data directory; a relative path is also resolved against the
    /// data directory. Environment variables such as %APPDATA% are expanded.
    /// </summary>
    public string LogDirectory { get; set; } = "";

    // OmdbUrl and ImdbSuggestionUrl, in the same "Filtarr" section, are Filtarr.Core.Configuration.ExternalApiOptions.

    public string ResolveLogDirectory()
    {
        var dir = Environment.ExpandEnvironmentVariables(LogDirectory ?? "");
        if (string.IsNullOrWhiteSpace(dir) || dir.Contains('%')) dir = "Logs";
        return Path.GetFullPath(dir, ResolveDataDirectory());
    }

    /// <summary>The data directory with environment variables expanded; falls back to the per-user application data folder.</summary>
    public string ResolveDataDirectory()
    {
        var expanded = Environment.ExpandEnvironmentVariables(DataDirectory ?? "");
        if (string.IsNullOrWhiteSpace(expanded) || expanded.Contains('%'))
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Filtarr");
        return Path.GetFullPath(expanded);
    }
}