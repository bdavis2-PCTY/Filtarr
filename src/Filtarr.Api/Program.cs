using Filtarr.Api;
using Filtarr.Api.Data;
using Filtarr.Api.Models;
using Filtarr.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Text.Json.Serialization;

// The build configuration decides the environment: Debug -> appsettings.Development.json, Release -> appsettings.Production.json.
// appsettings.json is always loaded first and the environment file (if present) overrides it.
#if DEBUG
const string BuildEnvironment = "Development";
#else
const string BuildEnvironment = "Production";
#endif
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, EnvironmentName = BuildEnvironment });
// builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
var options = builder.Configuration.GetSection(FiltarrOptions.Section).Get<FiltarrOptions>() ?? new FiltarrOptions();
var dataDir = options.ResolveDataDirectory();
Directory.CreateDirectory(dataDir);
builder.WebHost.UseUrls($"http://*:{options.Port}");
// Lets the same executable run as a Windows service (see scripts/Install-Service.ps1); a no-op when started from a console.
builder.Host.UseWindowsService(o => o.ServiceName = "Filtarr");

// Levels come from the "Serilog" config section; sinks are fixed: console + one file per day named after the date (yyyyMMdd.log).
var logDir = options.ResolveLogDirectory();
// The default level can be changed at runtime from the Settings page through this switch (Warning unless configured otherwise).
var levelSwitch = new LoggingLevelSwitch(
    Enum.TryParse<LogEventLevel>(builder.Configuration["Serilog:MinimumLevel:Default"], true, out var configuredLevel) ? configuredLevel : LogEventLevel.Warning);
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.ControlledBy(levelSwitch)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDir, ".log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(levelSwitch);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, "filtarr.db")}"));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("sonarr");
builder.Services.AddHttpClient("imdb", c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.UserAgent.ParseAdd("Filtarr"); });
// Short timeout + short-lived idle connections: OMDb drops idle keep-alive connections, and reusing a dead one hangs until the timeout.
builder.Services.AddHttpClient("omdb", c => c.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });
builder.Services.AddScoped<IOmdbClient, OmdbClient>();
builder.Services.AddScoped<IEpisodeRatingService, EpisodeRatingService>();
builder.Services.AddScoped<IImdbClient, ImdbClient>();
builder.Services.AddScoped<ISeriesCatalog, SeriesCatalog>();
builder.Services.AddScoped<ISeriesDetailsService, SeriesDetailsService>();
builder.Services.AddScoped<ISeriesAdder, SeriesAdder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SyncGate>();
builder.Services.AddSingleton<IFilterEngine, FilterEngine>();
builder.Services.AddScoped<ISonarrClient, SonarrClient>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ISettingsUpdater, SettingsUpdater>();
builder.Services.AddScoped<IFilterService, FilterService>();
builder.Services.AddScoped<IQuickFilterService, QuickFilterService>();
builder.Services.AddScoped<ISyncRunStore, SyncRunStore>();
builder.Services.AddScoped<IMonitoredEpisodeStore, MonitoredEpisodeStore>();
builder.Services.AddScoped<IMonitoredHistoryService, MonitoredHistoryService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.Logger.LogInformation("Filtarr data directory: {Dir}; logs: {LogDir}; port: {Port}", dataDir, logDir, options.Port);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    SchemaUpgrader.Upgrade(db, app.Logger);
    LegacyConfigImport.Run(db, app.Configuration, app.Logger);
    // A level saved in the UI wins over the configured one.
    if (LogLevels.TryParse(db.Settings.AsNoTracking().Select(s => s.LogLevel).FirstOrDefault(), out var savedLevel))
        levelSwitch.MinimumLevel = savedLevel;
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();

// Allows integration tests to use WebApplicationFactory<Program>.
public partial class Program;
