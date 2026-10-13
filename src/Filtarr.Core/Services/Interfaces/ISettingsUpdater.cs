using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>Validates user-entered settings (including live connection tests) before saving them.</summary>
public interface ISettingsUpdater
{
    /// <summary>
    /// Rejects an invalid URL or log level, a changed Sonarr URL/key that fails a connection test, and a new OMDb key that fails
    /// a test. Nothing is saved when it rejects.
    /// </summary>
    Task<SettingsUpdateResult> UpdateAsync(SettingsUpdate update, CancellationToken ct = default);
}
