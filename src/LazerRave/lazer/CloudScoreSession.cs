using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed class CloudScoreSession : IAsyncDisposable
{
    private readonly CloudClient client;
    private readonly Guid match;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly object gate = new();
    private GameplaySnapshot latest = GameplaySnapshot.Empty;
    private GameplaySnapshot? sent;
    private long sequence;
    public CloudScoreSession(CloudClient client, Guid match)
    {
        this.client = client; this.match = match;
        worker = Run();
    }
    public void Update(GameplaySnapshot value)
    {
        lock (gate)
        {
            if (latest.Finished) return;
            latest = value with { ExScore = Math.Max(latest.ExScore, value.ExScore), Misses = Math.Max(latest.Misses, value.Misses),
                MaxCombo = Math.Max(latest.MaxCombo, value.MaxCombo), Progress = Math.Max(latest.Progress, value.Progress) };
        }
    }
    private async Task Run()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                GameplaySnapshot score; lock (gate) score = latest;
                if (score == sent || score.Finished || client.Room?.MatchId != match || !client.Connected) continue;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await client.ReportScore(match, ++sequence, score, false, timeout.Token); sent = score;
                }
                catch (Exception error) when (!stop.IsCancellationRequested) { client.SetGameStatus("Score update delayed: " + error.Message); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    public async Task Finish()
    {
        stop.Cancel(); await worker;
        GameplaySnapshot score; lock (gate) score = latest;
        if (!score.Finished) score = score with { Finished = true, Aborted = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (true)
        {
            var room = client.Room;
            if (room?.MatchId != match || room.State == "results" || room.Members.FirstOrDefault(member => member.Id == client.User?.Id)?.Finished == true) return;
            try { await client.ReportScore(match, ++sequence, score, true, timeout.Token); return; }
            catch (Exception error) when (!timeout.IsCancellationRequested)
            {
                client.SetGameStatus("Sending round result: " + error.Message);
                await Task.Delay(400, timeout.Token);
            }
        }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); await worker; stop.Dispose(); }
}
