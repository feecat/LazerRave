using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed class LocalClearLampStore : IDisposable
{
    private readonly PlayRecordStore records;
    private readonly object gate = new();
    private readonly Dictionary<(string Path, string Player), Task<int>> cache = new();
    private int revision;
    public int Revision => Volatile.Read(ref revision);

    public LocalClearLampStore(PlayRecordStore records)
    {
        this.records = records;
        records.Changed += Invalidate;
    }

    public Task<int> GetClear(Chart chart, string player)
    {
        lock (gate)
        {
            var key = (Path.GetFullPath(chart.Path).ToUpperInvariant(), player);
            if (cache.TryGetValue(key, out var result)) return result;
            return cache[key] = Task.Run(async () =>
            {
                string hash = await PlayRecordStore.HashChart(chart.Path);
                return PlayRecordStore.BestClear(records.Read(hash), player);
            });
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            cache.Clear();
            Interlocked.Increment(ref revision);
        }
    }

    public void Dispose() => records.Changed -= Invalidate;
}
