using Filtarr.Core.Models;

namespace Filtarr.Core.Services.Interfaces;

/// <summary>User-editable settings stored in the database.</summary>
public interface ISettingsService
{
    Task<SettingsModel> GetAsync(CancellationToken ct = default);
    /// <summary>The Sonarr URL / keys in force, as saved on the Settings page.</summary>
    Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default);
    /// <summary>Saves without validating; use <see cref="ISettingsUpdater"/> for user input. Applies a changed log level immediately.</summary>
    Task UpdateAsync(SettingsUpdate update, CancellationToken ct = default);
}
