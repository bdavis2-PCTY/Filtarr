namespace Filtarr.Data.Models;

public enum FilterAction { Include, Exclude }

public class FilterCondition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = "";
}

/// <summary>
/// A filter is a set of conditions that must ALL match (AND). An episode is monitored when it matches
/// at least one enabled Include filter and no enabled Exclude filter. A filter with ImdbId == null is global; otherwise it only applies to the Sonarr series with that IMDb id.
/// </summary>
public class Filter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FilterAction Action { get; set; } = FilterAction.Include;
    public string? ImdbId { get; set; }
    public string? SeriesTitle { get; set; }
    public List<FilterCondition> Conditions { get; set; } = new();
}
