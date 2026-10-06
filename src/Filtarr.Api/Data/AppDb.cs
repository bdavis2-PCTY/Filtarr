using Filtarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Filtarr.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<Filter> Filters => Set<Filter>();
    public DbSet<QuickFilter> QuickFilters => Set<QuickFilter>();
    public DbSet<SyncRun> Runs => Set<SyncRun>();
    public DbSet<MonitoredEpisode> MonitoredEpisodes => Set<MonitoredEpisode>();
    public DbSet<SeasonRatings> SeasonRatings => Set<SeasonRatings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Filter>(e =>
        {
            e.OwnsMany(f => f.Conditions, c => c.ToJson());
            e.Property(f => f.Action).HasConversion<string>();
        });
        b.Entity<QuickFilter>(e =>
        {
            e.OwnsMany(f => f.Conditions, c => c.ToJson());
            e.Property(f => f.Action).HasConversion<string>();
        });
        b.Entity<SyncRun>().OwnsMany(r => r.Items, i => i.ToJson());
        b.Entity<MonitoredEpisode>().HasIndex(e => e.EpisodeId).IsUnique();
        b.Entity<SeasonRatings>(e =>
        {
            e.OwnsMany(s => s.Episodes, c => c.ToJson());
            e.HasIndex(s => new { s.SeriesImdbId, s.Season }).IsUnique();
        });
    }
}
