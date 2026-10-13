namespace Filtarr.Core.Models;

public enum FilterActionType { Include, Exclude }

public class FilterConditionModel
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = "";
}

/// <summary>
/// A filter is a set of conditions that must ALL match (AND). An episode is monitored when it matches
/// at least one enabled Include filter and no enabled Exclude filter. A filter with ImdbId == null is global; otherwise it only applies to the Sonarr series with that IMDb id.
/// </summary>
public class FilterModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FilterActionType Action { get; set; } = FilterActionType.Include;
    public string? ImdbId { get; set; }
    public string? SeriesTitle { get; set; }
    public List<FilterConditionModel> Conditions { get; set; } = new();
}

/// <summary>
/// A reusable filter template (name, action and conditions) that is not tied to any series and is never evaluated itself.
/// Picking one on a series page pre-fills a new, unsaved series filter.
/// </summary>
public class QuickFilterModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FilterActionType Action { get; set; } = FilterActionType.Include;
    public List<FilterConditionModel> Conditions { get; set; } = new();
}
