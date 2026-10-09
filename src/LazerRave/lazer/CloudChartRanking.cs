using System.Net.Http.Json;
using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed record RankingChart(Guid Id, string Title, string Sha256, string? Md5);
internal sealed record ChartRank(long Rank, string Username, string DisplayName, int ExScore, int? ScoreMax, int Misses,
    int MaxCombo, string Clear, DateTimeOffset CreatedAt, bool Verified, Guid Id = default, Guid ClientRunId = default,
    int Perfect = 0, int Great = 0, int Good = 0, int Bad = 0, int Poor = 0, int? NormalScore = null,
    string Arrangement = "off", string Gauge = "normal", string? BestClear = null, int? MinMisses = null);

internal sealed partial class CloudClient
{
    private static readonly HttpClient publicRanking = new(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri("https://lazerrave.com/"), Timeout = TimeSpan.FromSeconds(20) };
    public async Task<RankingChart?> FindRankingChart(Chart chart, string hash, CancellationToken cancellation)
    {
        // A title narrows the published catalog; only a matching file hash identifies the chart.
        var connection = http ?? publicRanking;
        for (int page = 1; page <= 20; page++)
        {
            using var response = await connection.GetAsync($"api/charts?q={Uri.EscapeDataString(chart.Title)}&keys={chart.Keys}&page={page}", cancellation);
            await Check(response, cancellation);
            var charts = await response.Content.ReadFromJsonAsync<RankingChart[]>(json, cancellation) ?? [];
            var match = charts.FirstOrDefault(candidate => string.Equals(candidate.Sha256, hash, StringComparison.OrdinalIgnoreCase) ||
                chart.Md5 is not null && string.Equals(candidate.Md5, chart.Md5, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            if (charts.Length < 50) return null;
        }
        return null;
    }
    public async Task<ChartRank[]> ChartRanking(Guid chart, string arrangement, string gauge, int page, CancellationToken cancellation)
    {
        using var response = await (http ?? publicRanking).GetAsync($"api/rankings/{chart}?arrangement={Uri.EscapeDataString(arrangement)}&gauge={Uri.EscapeDataString(gauge)}&page={page}", cancellation);
        await Check(response, cancellation);
        return await response.Content.ReadFromJsonAsync<ChartRank[]>(json, cancellation) ?? [];
    }
    public async Task SubmitRecord(Chart chart, PlayRecord record, CancellationToken cancellation)
    {
        if (User is null || http is null) throw new InvalidOperationException("Sign in to submit scores.");
        if (!record.Ranked) throw new InvalidOperationException("Practice scores cannot be submitted.");
        var target = await FindRankingChart(chart, record.ChartHash, cancellation) ?? throw new InvalidOperationException("This chart is not in the published ranking catalog.");
        var score = record.Score;
        using var response = await http.PostAsJsonAsync("api/scores", new
        {
            ChartId = target.Id, ClientRunId = record.Id, Ruleset = "openlr2-v1", record.Arrangement, record.Gauge,
            score.Perfect, score.Great, score.Good, score.Bad, score.Poor, score.MaxCombo, score.NormalScore,
            Clear = score.ClearType switch { 2 => "easy", 3 => "normal", 4 => "hard", 5 => "full-combo", _ => "failed" },
            ScoreMax = score.TotalNotes * 2, InputType = "keyboard",
        }, json, cancellation);
        await Check(response, cancellation);
    }
}
