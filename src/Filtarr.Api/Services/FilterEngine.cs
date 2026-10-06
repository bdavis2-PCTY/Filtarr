using System.Globalization;
using System.Text.RegularExpressions;
using Filtarr.Api.Models;

namespace Filtarr.Api.Services;

public class FilterEngine : IFilterEngine
{
    public Dictionary<string, object?> BuildContext(SonarrSeries s, SonarrEpisode e, DateTime nowUtc, double? episodeRating = null) => new()
    {
        ["series.rating"] = s.Ratings is { Votes: > 0 } r ? r.Value : (double?)null,
        ["series.votes"] = s.Ratings?.Votes,
        ["series.year"] = s.Year == 0 ? null : s.Year,
        ["series.genres"] = s.Genres ?? new List<string>(),
        ["series.network"] = s.Network,
        ["series.status"] = s.Status,
        ["series.type"] = s.SeriesType,
        ["series.certification"] = s.Certification,
        ["series.language"] = s.OriginalLanguage?.Name,
        ["episode.season"] = e.SeasonNumber,
        ["episode.number"] = e.EpisodeNumber,
        ["episode.title"] = e.Title,
        ["episode.overview"] = e.Overview,
        ["episode.airDate"] = e.AirDateUtc,
        ["episode.daysSinceAired"] = e.AirDateUtc is { } d ? (nowUtc.Date - d.Date).TotalDays : (double?)null,
        ["episode.runtime"] = e.Runtime == 0 ? null : e.Runtime,
        ["episode.hasFile"] = e.HasFile,
        ["episode.isFinale"] = !string.IsNullOrEmpty(e.FinaleType) && e.FinaleType != "midseason",
        ["episode.isSpecial"] = e.SeasonNumber == 0,
        ["episode.rating"] = episodeRating,
    };

    public bool Matches(Filter f, Dictionary<string, object?> ctx) =>
        f.Conditions.Count > 0 && f.Conditions.All(c => Evaluate(c, ctx));

    /// <summary>Missing values never match (so "rating &lt; 5" does not match an unrated show).</summary>
    public bool Evaluate(FilterCondition c, Dictionary<string, object?> ctx)
    {
        if (!ctx.TryGetValue(c.Field, out var actual) || actual is null) 
			return false;
        var op = c.Operator;
        var expected = c.Value ?? "";
        switch (actual)
        {
            case List<string> list:
            {
                bool Any(Func<string, bool> p) => list.Any(p);
                return op switch
                {
                    "eq" => Any(x => Eq(x, expected)),
                    "neq" => !Any(x => Eq(x, expected)),
                    "in" => Any(x => Split(expected).Contains(x, StringComparer.OrdinalIgnoreCase)),
                    "contains" => Any(x => x.Contains(expected, StringComparison.OrdinalIgnoreCase)),
                    "notContains" => !Any(x => x.Contains(expected, StringComparison.OrdinalIgnoreCase)),
                    _ => false,
                };
            }
            
			case bool b:
                return bool.TryParse(expected, out var eb) && (op == "neq" ? b != eb : b == eb);
            
			case DateTime dt:
                if (!DateTime.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ed)) return false;
                return Compare(dt.Date.CompareTo(ed.Date), op);
            
			case string s:
                return op switch
                {
                    "eq" => Eq(s, expected),
                    "neq" => !Eq(s, expected),
                    "in" => Split(expected).Contains(s, StringComparer.OrdinalIgnoreCase),
                    "contains" => s.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    "notContains" => !s.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    "startsWith" => s.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
                    "endsWith" => s.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
                    "regex" => SafeRegex(s, expected),
                    _ => false,
                };
            default:
                if (!double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var en)) 
					return false;
                return Compare(Convert.ToDouble(actual, CultureInfo.InvariantCulture).CompareTo(en), op);
        }
    }

    static bool Compare(int cmp, string op) => op switch
    {
        "eq" => cmp == 0, 
		"neq" => cmp != 0, 
		"gt" => cmp > 0, 
		"gte" => cmp >= 0, 
		"lt" => cmp < 0, 
		"lte" => cmp <= 0, 
		_ => false,
    };
	
    static bool Eq(string a, string b) 
		=> string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		
    static string[] Split(string v) 
		=> v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		
    static bool SafeRegex(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
