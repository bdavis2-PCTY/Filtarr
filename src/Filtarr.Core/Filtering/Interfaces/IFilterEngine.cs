using Filtarr.Core.Clients.Models;
using Filtarr.Core.Models;

namespace Filtarr.Core.Filtering.Interfaces;

/// <summary>Pure filter evaluation; no I/O.</summary>
public interface IFilterEngine
{
    Dictionary<string, object?> BuildContext(SonarrSeries series, SonarrEpisode episode, DateTime nowUtc, double? episodeRating = null);
    bool Matches(FilterModel filter, Dictionary<string, object?> context);
    bool Evaluate(FilterConditionModel condition, Dictionary<string, object?> context);
}
