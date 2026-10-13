namespace Filtarr.Data.Models;

/// <summary>
/// A reusable filter template (name, action and conditions) that is not tied to any series and is never evaluated itself.
/// Picking one on a series page pre-fills a new, unsaved series filter.
/// </summary>
public class QuickFilter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public FilterAction Action { get; set; } = FilterAction.Include;
    public List<FilterCondition> Conditions { get; set; } = new();
}
