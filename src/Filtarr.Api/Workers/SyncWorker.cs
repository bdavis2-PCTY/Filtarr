using Filtarr.Core.Services.Interfaces;

namespace Filtarr.Api.Workers;

/// <summary>Runs the automatic sync in the background when it is enabled in the settings.</summary>
public class SyncWorker(IServiceScopeFactory scopes, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        DateTime? last = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var s = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync(ct);
                var now = clock.GetUtcNow().UtcDateTime;
                var due = last is null || now - last >= TimeSpan.FromMinutes(Math.Max(1, s.SyncIntervalMinutes));
                if (s.AutoSyncEnabled && due)
                {
                    last = now;
                    await scope.ServiceProvider.GetRequiredService<ISyncService>().RunAsync("auto", ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* RunAsync records its own failures; settings read errors retry next tick */ }
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ContinueWith(_ => { });
        }
    }
}
