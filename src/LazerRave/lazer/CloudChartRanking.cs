using System.Net.Http.Json;
using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed record RankingChart(Guid Id, string Title, string Sha256, string? Md5, Guid ChartId = default, string Visibility = "public");
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
        using var response = await (http ?? publicRanking).GetAsync($"api/charts/resolve?sha256={Uri.EscapeDataString(hash)}", cancellation);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await Check(response, cancellation);
        var match = await response.Content.ReadFromJsonAsync<RankingChart>(json, cancellation);
        return match is not null && string.Equals(match.Sha256, hash, StringComparison.OrdinalIgnoreCase) ? match : null;
    }
    public async Task<ChartRank[]> ChartRanking(Guid chart, string arrangement, string gauge, int page, CancellationToken cancellation)
    {
        using var response = await (http ?? publicRanking).GetAsync($"api/rankings/{chart}?arrangement={Uri.EscapeDataString(arrangement)}&gauge={Uri.EscapeDataString(gauge)}&page={page}", cancellation);
        await Check(response, cancellation);
        return await response.Content.ReadFromJsonAsync<ChartRank[]>(json, cancellation) ?? [];
    }
    public async Task SubmitRecord(Chart chart, PlayRecord record, CancellationToken cancellation, bool privateRanking = false)
    {
        if (User is null || http is null) throw new InvalidOperationException("Sign in to submit scores.");
        if (!string.Equals(record.Player, User.Username, StringComparison.Ordinal)) throw new InvalidOperationException("Sign in with the account that played this record.");
        var submissionClient = http;
        if (!record.Ranked) throw new InvalidOperationException("Practice scores cannot be submitted.");
        var target = privateRanking ? null : await FindRankingChart(chart, record.ChartHash, cancellation);
        if (target is null)
        {
            var metadata = await Task.Run(() =>
            {
                if (new FileInfo(chart.Path).Length > 16 * 1024 * 1024) throw new InvalidOperationException("Chart is too large to register.");
                var bytes = File.ReadAllBytes(chart.Path);
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if (!sha.Equals(record.ChartHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The chart changed after this play. Restore the original file before submitting.");
                return new
                {
                    Sha256 = sha, Md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant(),
                    Title = chart.FullTitle, chart.Artist, Difficulty = chart.Label, chart.Keys, chart.Level, chart.Bpm,
                    LengthMs = BmsTimeline.Read(chart, encoding()).Length, Visibility = privateRanking ? "restricted" : "public",
                };
            }, cancellation);
            using var registration = await submissionClient.PostAsJsonAsync("api/charts/register", metadata, json, cancellation);
            await Check(registration, cancellation);
            target = await registration.Content.ReadFromJsonAsync<RankingChart>(json, cancellation) ?? throw new InvalidOperationException("The ranking registration returned no chart.");
        }
        var score = record.Score;
        using var response = await submissionClient.PostAsJsonAsync("api/scores", new
        {
            ChartId = target.ChartId == Guid.Empty ? target.Id : target.ChartId, BoardId = target.Id, ClientRunId = record.Id, Ruleset = "openlr2-v1", record.Arrangement, record.Gauge,
            score.Perfect, score.Great, score.Good, score.Bad, score.Poor, score.MaxCombo, score.NormalScore,
            Clear = score.ClearType switch { 2 => "easy", 3 => "normal", 4 => "hard", 5 => "full-combo", _ => "failed" },
            ScoreMax = score.TotalNotes * 2, InputType = "keyboard", PlayedAt = record.PlayedAt,
        }, json, cancellation);
        await Check(response, cancellation);
    }
}
