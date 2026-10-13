namespace Filtarr.Core.Synchronization;

/// <summary>Process-wide lock so only one sync runs at a time (the services themselves are scoped).</summary>
public class SyncGate
{
    readonly SemaphoreSlim _sem = new(1, 1);
    public bool TryEnter() => _sem.Wait(0);
    public void Exit() => _sem.Release();
}
