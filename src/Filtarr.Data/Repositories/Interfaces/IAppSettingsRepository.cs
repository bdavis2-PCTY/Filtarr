using Filtarr.Data.Models;

namespace Filtarr.Data.Repositories.Interfaces;

/// <summary>The single row of user-editable settings.</summary>
public interface IAppSettingsRepository
{
    /// <summary>The settings row, or null before anything has been saved.</summary>
    Task<AppSettings?> GetAsync(CancellationToken ct = default);

    /// <summary>Creates the settings row, or overwrites it with <paramref name="settings"/>.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
