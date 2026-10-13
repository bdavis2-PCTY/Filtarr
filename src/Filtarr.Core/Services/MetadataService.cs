using Filtarr.Core.Clients.Interfaces;
using Filtarr.Core.Filtering;
using Filtarr.Core.Models;
using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Core.Services;

public class MetadataService(ISonarrClient sonarr) : IMetadataService
{
    public IReadOnlyList<FieldDef> GetFields() => FilterFields.All;

    public async Task<List<SonarrSeriesSummary>> ListSonarrSeriesAsync(CancellationToken ct = default) =>
        (await sonarr.GetSeriesAsync(ct)).OrderBy(s => s.Title).Select(s => new SonarrSeriesSummary(s.Id, s.Title, s.Year, s.Monitored)).ToList();
}
