using Serilog.Events;

namespace Filtarr.Core.Configuration;

public static class LogLevels
{
    /// <summary>The levels offered in the UI, most to least verbose.</summary>
    public static readonly string[] Allowed = ["Verbose", "Information", "Warning", "Error"];

    public static bool TryParse(string? value, out LogEventLevel level)
    {
        level = default;
        var match = Allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is not null && Enum.TryParse(match, out level);
    }
}
