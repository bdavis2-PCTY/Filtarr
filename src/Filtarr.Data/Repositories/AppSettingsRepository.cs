using Filtarr.Data.Models;
using Filtarr.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Data.Repositories;

internal sealed class AppSettingsRepository(AppDb db) : IAppSettingsRepository
{
    public Task<AppSettings?> GetAsync(CancellationToken ct = default) =>
        db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        var row = await db.Settings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.Settings.Add(settings);
        }
        else
        {
            var id = row.Id;
            db.Entry(row).CurrentValues.SetValues(settings);
            row.Id = id; // there is only ever one row; never change its key
        }
        await db.SaveChangesAsync(ct);
    }
}
