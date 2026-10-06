using Filtarr.Api.Models;
using Filtarr.Api.Services;

namespace Filtarr.Api.Tests;

public class FilterEngineTests
{
    static readonly DateTime Now = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    readonly FilterEngine _engine = new();

    static SonarrSeries Series(double? rating = 8.5, params string[] genres) => new(
        1, "Show", 2020, true, "continuing", "HBO", "standard", "TV-MA", 45, genres.ToList(),
        rating is null ? null : new SonarrRatings(rating.Value, 100), new SonarrLanguage("English"), null);

    static SonarrEpisode Episode(int season = 1, int number = 1, DateTime? aired = null, string? finale = null) =>
        new(10, 1, "Pilot", season, number, null, aired, 45, false, false, finale);

    bool Match(string field, string op, string value, SonarrSeries? s = null, SonarrEpisode? e = null)
    {
        var ctx = _engine.BuildContext(s ?? Series(), e ?? Episode(aired: Now.AddDays(-3)), Now);
        return _engine.Evaluate(new FilterCondition { Field = field, Operator = op, Value = value }, ctx);
    }

    [Theory]
    [InlineData("gte", "8", true)]
    [InlineData("gt", "8.5", false)]
    [InlineData("lt", "9", true)]
    [InlineData("eq", "8.5", true)]
    public void Number_comparisons(string op, string value, bool expected) =>
        Assert.Equal(expected, Match("series.rating", op, value));

    [Fact]
    public void Missing_values_never_match() =>
        Assert.False(Match("series.rating", "lt", "5", Series(rating: null)));

    [Fact]
    public void Genres_use_any_semantics() => Assert.True(Match("series.genres", "eq", "drama", Series(8, "Drama", "Crime")));

    [Fact]
    public void Genres_neq_means_none_of_them() => Assert.False(Match("series.genres", "neq", "Drama", Series(8, "Drama")));

    [Fact]
    public void String_in_operator_is_case_insensitive() => Assert.True(Match("series.network", "in", "abc, hbo"));

    [Fact]
    public void Date_comparison_ignores_time_of_day() =>
        Assert.True(Match("episode.airDate", "gte", "2024-05-29"));

    [Fact]
    public void DaysSinceAired_is_relative_to_supplied_clock() =>
        Assert.True(Match("episode.daysSinceAired", "eq", "3"));

    [Fact]
    public void Finale_flag_ignores_midseason() =>
        Assert.False(Match("episode.isFinale", "eq", "true", e: Episode(finale: "midseason")));

    [Fact]
    public void Invalid_regex_does_not_throw() => Assert.False(Match("episode.title", "regex", "("));

    [Fact]
    public void Filter_without_conditions_matches_nothing() =>
        Assert.False(_engine.Matches(new Filter(), _engine.BuildContext(Series(), Episode(), Now)));

    [Fact]
    public void All_conditions_must_match()
    {
        var f = new Filter
        {
            Conditions =
            {
                new() { Field = "series.rating", Operator = "gte", Value = "8" },
                new() { Field = "episode.season", Operator = "eq", Value = "2" },
            },
        };
        Assert.False(_engine.Matches(f, _engine.BuildContext(Series(), Episode(season: 1), Now)));
        Assert.True(_engine.Matches(f, _engine.BuildContext(Series(), Episode(season: 2), Now)));
    }
}
