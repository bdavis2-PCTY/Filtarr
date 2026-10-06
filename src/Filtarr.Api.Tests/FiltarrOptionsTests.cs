namespace Filtarr.Api.Tests;

public class FiltarrOptionsTests
{
    static readonly string Data = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "filtarr-data"));

    [Fact]
    public void Log_directory_defaults_to_Logs_inside_the_data_directory() =>
        Assert.Equal(Path.Combine(Data, "Logs"), new FiltarrOptions { DataDirectory = Data }.ResolveLogDirectory());

    [Fact]
    public void Relative_log_directory_is_resolved_against_the_data_directory() =>
        Assert.Equal(Path.Combine(Data, "my", "logs"), new FiltarrOptions { DataDirectory = Data, LogDirectory = "my/logs" }.ResolveLogDirectory());

    [Fact]
    public void Absolute_log_directory_is_used_as_is()
    {
        var abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere"));
        Assert.Equal(abs, new FiltarrOptions { DataDirectory = Data, LogDirectory = abs }.ResolveLogDirectory());
    }

    [Fact]
    public void Environment_variables_are_expanded_in_the_log_directory()
    {
        Environment.SetEnvironmentVariable("FILTARR_TEST_LOGS", Path.Combine(Path.GetTempPath(), "envlogs"));
        var dir = new FiltarrOptions { DataDirectory = Data, LogDirectory = "%FILTARR_TEST_LOGS%" }.ResolveLogDirectory();
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "envlogs")), dir);
    }
}
