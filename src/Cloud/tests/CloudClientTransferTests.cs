using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LazerRave.Lazer;
using Npgsql;
using Xunit;

namespace Cloud.Tests;

public sealed class LoopbackFactAttribute : FactAttribute
{
    public LoopbackFactAttribute(bool windowsOnly = false)
    {
        if (Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL") is null) Skip = "Requires the isolated loopback cloud test server.";
        if (windowsOnly && !OperatingSystem.IsWindows()) Skip = "Requires Windows session encryption.";
    }
}

public sealed class CloudClientTransferTests
{
    [LoopbackFact(windowsOnly: true)]
    public async Task SessionSurvivesRestartButSignOutAndServerRevocationRequireLogin()
    {
        var url = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL")!;
        Assert.True(new Uri(url).IsLoopback);
        var root = Path.Combine(Path.GetTempPath(), "lazerrave-session-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "userdata", "cloud-session.bin");
        var store = new CloudSessionStore(path);
        var username = "session_" + Guid.NewGuid().ToString("N")[..12];
        const string password = "saved-session-test-password";
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
        using var registration = await http.PostAsJsonAsync("/api/auth/register", new { username, email = username + "@example.com", password });
        Assert.True(registration.IsSuccessStatusCode);
        CloudClient Client() => new(() => [], () => "auto", _ => Task.CompletedTask, root, store);
        try
        {
            CloudUser identity;
            await using (var original = Client())
            {
                await original.Login(url, username, password, default);
                identity = original.User!;
                var saved = store.Read()!;
                Assert.DoesNotContain(saved.Token, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
                Assert.Equal(CloudClient.ServerUri(url).AbsoluteUri, saved.Server);
            }
            Assert.True(store.Exists);
            await using (var restarted = Client())
            {
                await restarted.Restore(default);
                Assert.Equal(identity, restarted.User); Assert.True(restarted.Connected);
                await restarted.Disconnect();
                Assert.False(store.Exists); Assert.Null(restarted.User);
            }
            await using (var signedOut = Client()) { await signedOut.Restore(default); Assert.Null(signedOut.User); }
            await using (var renewed = Client()) { await renewed.Login(url, username, password, default); }
            var revoked = store.Read()!;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", revoked.Token);
            using var logout = await http.PostAsync("/api/auth/logout", null);
            Assert.True(logout.IsSuccessStatusCode);
            await using (var expired = Client()) { await expired.Restore(default); Assert.Null(expired.User); Assert.False(store.Exists); }
            store.Save(new Uri("http://127.0.0.1:1"), revoked.Token);
            await using (var offline = Client()) { await offline.Restore(default); Assert.Null(offline.User); Assert.True(store.Exists); }
            Assert.Equal(revoked.Token, store.Read()!.Token);
            await File.WriteAllTextAsync(path, "damaged session file");
            Assert.Null(store.Read()); Assert.False(store.Exists);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [LoopbackFact]
    public async Task PasswordLoginDoesNotRequireEmailOrAnAvailableMultiplayerConnection()
    {
        var url = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL")!;
        Assert.True(new Uri(url).IsLoopback);
        var username = "login_" + Guid.NewGuid().ToString("N")[..12];
        const string password = "desktop-account-test-password";
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
        using var registration = await http.PostAsJsonAsync("/api/auth/register", new { username, email = username + "@example.com", password });
        Assert.True(registration.IsSuccessStatusCode);
        await using var first = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        await using var second = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        await using var third = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        await using var fourth = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        await using var fifth = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        await Assert.ThrowsAsync<HttpRequestException>(() => first.Login(url, username, "incorrect", default));
        Assert.Null(first.User);
        await first.Login(url, username, password, default);
        Assert.Equal(username, first.User!.Username); Assert.True(first.Connected); Assert.True(first.User.Uid > 0);
        await second.Login(url, username, password, default);
        Assert.True(second.Connected);
        Assert.Equal(first.User.Id, second.User!.Id);
        await first.CreateRoom("Desktop with website online", default);
        await Wait(() => second.Rooms.Any(room => room.Id == first.Room!.Id));
        await second.Disconnect();
        await Wait(() => first.Room is { Members.Length: 1 });
        await second.Login(url, username, password, default);
        await third.Login(url, username, password, default);
        await fourth.Login(url, username, password, default);
        Assert.True(third.Connected); Assert.True(fourth.Connected);
        await fifth.Login(url, username, password, default);
        await Wait(() => !fifth.Connected);
        Assert.Equal(first.User.Id, fifth.User!.Id);
        await first.Disconnect();
        await fifth.Connect(default);
        Assert.True(fifth.Connected); Assert.Equal(username, fifth.User.Username);
    }

    private static async Task Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Cloud client did not reach the expected state."); await Task.Delay(50); }
    }
    [LoopbackFact]
    public async Task ChartOnlyMatchHostTransferLiveScoresAndRoundResults()
    {
        var url = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL")!;
        Assert.True(new Uri(url).IsLoopback);
        var root = Path.Combine(Path.GetTempPath(), "lazerrave-score-flow-" + Guid.NewGuid());
        var first = Path.Combine(root, "host", "Original song"); var second = Path.Combine(root, "guest", "Renamed song");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        var original = Path.Combine(first, "normal.bms"); var renamed = Path.Combine(second, "different-name.bms");
        const string chart = "#TITLE Missing resource fixture\n#ARTIST Test\n#BPM 120\n#WAV01 absent.ogg\n#BMP01 absent.avi\n#00111:01\n";
        File.WriteAllText(original, chart); File.WriteAllText(renamed, chart);
        File.WriteAllBytes(Path.Combine(second, "unrelated.wav"), new byte[32]);
        var suffix = Guid.NewGuid().ToString("N")[..12]; const string password = "isolated-score-flow-password";
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
        try
        {
            foreach (var username in new[] { "score_host_" + suffix, "score_guest_" + suffix })
                using (var registration = await http.PostAsJsonAsync("api/auth/register", new { username, password, email = username + "@example.com" }))
                    Assert.True(registration.IsSuccessStatusCode, await registration.Content.ReadAsStringAsync());
            await using var host = new CloudClient(() => [first], () => "auto", _ => Task.CompletedTask, Path.Combine(root, "host"));
            await using var guest = new CloudClient(() => [second], () => "auto", _ => Task.CompletedTask, Path.Combine(root, "guest"));
            await host.Login(url, "score_host_" + suffix, password, default); await guest.Login(url, "score_guest_" + suffix, password, default);
            await host.CreateRoom("BMS-only match and live scoreboard", default); await guest.JoinRoom(host.Room!.Id, default);
            await Wait(() => host.Room!.Members.Length == 2);
            await host.SelectChart(new(original, "Missing resource fixture", "Test", 7, 1), default);
            await Wait(() => host.AvailableChart == original && guest.AvailableChart == renamed && host.Room!.Members.All(value => value.ContentState == "available") && guest.Room!.Version == host.Room.Version);
            Assert.Null(host.Room!.Chart!.ContentSha256);
            await Assert.ThrowsAnyAsync<Exception>(() => guest.TransferHost(host.User!.Id, default));
            await host.TransferHost(guest.User!.Id, default);
            await Wait(() => guest.Room!.HostId == guest.User!.Id && host.Room!.Version == guest.Room.Version);
            await host.Ready(default); await Wait(() => guest.Room!.Members.Single(value => value.Id == host.User!.Id).Ready);
            await guest.Ready(default); await Wait(() => guest.CanStartRound);
            await guest.StartRound(default);
            await Wait(() => host.Room!.MatchId == guest.Room!.MatchId && host.Room.StartAt is { } at && host.ServerNow >= at.AddMilliseconds(100));
            var match = host.Room!.MatchId!.Value;
            await using var hostScores = new CloudScoreSession(host, match);
            await using var guestScores = new CloudScoreSession(guest, match);
            hostScores.Update(new(100, 20, 25, 2, .3, 0, false, false)); guestScores.Update(new(150, 15, 40, 1, .4, 0, false, false));
            await Wait(() => host.Room!.Members.First().Id == guest.User!.Id && host.Room.Members.First().ExScore == 150 && guest.Room!.Members.Single(value => value.Id == host.User!.Id).ExScore == 100);
            hostScores.Update(new(240, 5, 35, 2, .7, 0, false, false));
            await Wait(() => guest.Room!.Members.First().Id == host.User!.Id && guest.Room.Members.First().MaxCombo == 35);
            hostScores.Update(new(300, 8, 35, 2, 1, 3, true, false)); await hostScores.Finish();
            await Wait(() => guest.Room!.Members.Single(value => value.Id == host.User!.Id).Finished);
            Assert.Equal("playing", guest.Room!.State);
            guestScores.Update(new(350, 30, 50, 1, 1, 4, true, false)); await guestScores.Finish();
            await Wait(() => host.Room!.State == "results" && host.Room.Results?.Length == 2);
            var results = host.Room!.Results!;
            Assert.Equal(guest.User!.Id, results[0].Id); Assert.Equal(350, results[0].ExScore); Assert.Equal(50, results[0].MaxCombo); Assert.Equal(4, results[0].ClearType);
            Assert.All(results, value => { Assert.True(value.Finished); Assert.False(value.Aborted); Assert.True(value.Uid > 0); });
        }
        finally { Directory.Delete(root, true); }
    }
    [LoopbackFact]
    public async Task HostStartsOnlyAfterBothClientsVerifyAndExplicitlyReady()
    {
        var url = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL")!;
        Assert.True(new Uri(url).IsLoopback);
        var root = Path.Combine(Path.GetTempPath(), "lazerrave-ready-flow-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var chart = Path.Combine(root, "normal.bms");
        File.WriteAllText(chart, "#TITLE Ready fixture\n#ARTIST Test\n#BPM 120\n#WAV01 tone.wav\n#00111:01\n");
        File.WriteAllBytes(Path.Combine(root, "tone.wav"), new byte[64]);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        const string password = "isolated-ready-flow-password";
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
        try
        {
            foreach (var username in new[] { "ready_host_" + suffix, "ready_guest_" + suffix })
                using (var registration = await http.PostAsJsonAsync("api/auth/register", new { username, password, email = username + "@example.com" }))
                    Assert.True(registration.IsSuccessStatusCode, await registration.Content.ReadAsStringAsync());
            await using var host = new CloudClient(() => [root], () => "auto", _ => Task.CompletedTask, root);
            await using var guest = new CloudClient(() => [root], () => "auto", _ => Task.CompletedTask, root);
            await host.Login(url, "ready_host_" + suffix, password, default);
            await guest.Login(url, "ready_guest_" + suffix, password, default);
            await host.CreateRoom("Ready flow fixture", default);
            await guest.JoinRoom(host.Room!.Id, default);
            await Wait(() => host.Room!.Members.Length == 2);
            await host.SelectChart(new(chart, "Ready fixture", "Test", 7, 1), default);
            await Wait(() => host.AvailableChart is not null && guest.AvailableChart is not null
                && host.Room!.Members.All(member => member.ContentState == "available")
                && guest.Room!.Version == host.Room.Version);
            Assert.Equal("lobby", host.Room!.State); Assert.Null(host.Room.MatchId);
            Assert.All(host.Room.Members, member => Assert.False(member.Ready));
            Assert.False(host.CanStartRound);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartRound(default));
            await guest.Ready(default);
            await Wait(() => host.Room!.Members.Single(member => member.Id == guest.User!.Id).Ready);
            Assert.False(host.CanStartRound);
            await host.Ready(default);
            await Wait(() => host.CanStartRound && guest.Room!.Members.All(member => member.Ready));
            Assert.False(guest.CanStartRound);
            Assert.Equal("lobby", guest.Room!.State); Assert.Null(guest.Room.MatchId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => guest.StartRound(default));
            // Changing even the same chart invalidates all previous Ready confirmations.
            await host.SelectChart(new(chart, "Ready fixture", "Test", 7, 1), default);
            await Wait(() => host.Room!.Members.All(member => member.ContentState == "available" && !member.Ready)
                && guest.Room!.Version == host.Room.Version);
            Assert.False(host.CanStartRound);
            await guest.Ready(default);
            await Wait(() => host.Room!.Members.Single(member => member.Id == guest.User!.Id).Ready);
            await host.Ready(default);
            await Wait(() => host.CanStartRound);
            await host.StartRound(default);
            await Wait(() => guest.Room!.MatchId == host.Room!.MatchId && guest.Room.StartAt is not null);
            Assert.NotNull(host.Room!.MatchId);
            Assert.Equal(host.Room.StartAt, guest.Room!.StartAt);
            Assert.Contains(host.Room.State, new[] { "countdown", "playing" });
            Assert.False(host.CanStartRound);
            Assert.InRange(host.Room.StartAt!.Value - host.ServerNow, TimeSpan.Zero, TimeSpan.FromSeconds(4));
        }
        finally { Directory.Delete(root, true); }
    }
    [LoopbackFact]
    public async Task TwoDesktopClientsShareDownloadVerifyAndExpireASong()
    {
        var url = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL")!;
        Assert.True(new Uri(url).IsLoopback);
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")!;
        Assert.Contains("lazerrave_test", connection);
        var root = Path.Combine(Path.GetTempPath(), "lazerrave-client-test-" + Guid.NewGuid());
        var source = Path.Combine(root, "host", "library", "Fixture"); Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "normal.bms"), "#TITLE Shared fixture\n#ARTIST Test\n#WAV01 tone.wav\n#00118:01\n");
            File.WriteAllText(Path.Combine(source, "another.bms"), "#TITLE Shared fixture\n#ARTIST Test\n#WAV01 tone.wav\n#00118:0101\n");
            var audio = new byte[9 * 1024 * 1024]; RandomNumberGenerator.Fill(audio); File.WriteAllBytes(Path.Combine(source, "tone.wav"), audio);
            using var http = new HttpClient { BaseAddress = new Uri(url) }; http.DefaultRequestHeaders.Add("X-LazerRave", "1");
            var suffix = Guid.NewGuid().ToString("N")[..12]; var password = "cloud-transfer-test-password";
            foreach (var name in new[] { "host_" + suffix, "guest_" + suffix, "other_" + suffix })
                using (var response = await http.PostAsJsonAsync("/api/auth/register", new { username = name, email = name + "@example.com", password })) Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var guestRoot = Path.Combine(root, "guest"); string? imported = null;
            await using var host = new CloudClient(() => new[] { source }, () => "auto", _ => Task.CompletedTask, Path.Combine(root, "host"));
            await using var guest = new CloudClient(() => Directory.Exists(Path.Combine(guestRoot, "Shared")) ? Directory.EnumerateDirectories(Path.Combine(guestRoot, "Shared")).Where(p => Path.GetFileName(p) != ".incoming").ToArray() : [],
                () => "auto", path => { imported = path; return Task.CompletedTask; }, guestRoot);
            await host.Login(url, "host_" + suffix, password, default); await guest.Login(url, "guest_" + suffix, password, default);
            await host.CreateRoom("Transfer test", default); await guest.JoinRoom(host.Room!.Id, default);
            await Wait(() => host.Room?.Members.Length == 2);
            Assert.All(host.Room!.Members, member => Assert.True(member.Uid > 0));
            Assert.Equal(guest.User!.Uid, host.Room.Members.Single(member => member.Id == guest.User.Id).Uid);
            var channel = host.Room.Id.ToString();
            await host.SendChat(channel, "Ready to share the selected song.", default);
            await Wait(() => guest.Messages.Any(message => message.Channel == channel));
            await guest.LoadChat(channel, default);
            var chat = Assert.Single(guest.Messages.Where(message => message.Channel == channel));
            Assert.Equal(host.User!.Id, chat.UserId);
            Assert.Equal("Ready to share the selected song.", chat.Text);
            await host.SelectChart(new(Path.Combine(source, "normal.bms"), "Shared fixture", "Test", 7, 1), default);
            await Wait(() => host.AvailableChart is not null && guest.Room?.Members.Single(m => m.Id == guest.User!.Id).ContentState == "missing");
            Assert.Null(guest.AvailableChart); Assert.False(Directory.Exists(Path.Combine(guestRoot, "Shared")));
            bool cancelledUpload = false;
            Action stopUpload = () => { if (!cancelledUpload && host.Progress?.Stage == "Uploading" && host.Progress.Completed > 0) { cancelledUpload = true; host.CancelTransfer(); } };
            host.Changed += stopUpload;
            await host.Upload(default); host.Changed -= stopUpload;
            Assert.True(cancelledUpload); Assert.Null(host.Room!.Chart!.ShareId);
            await host.Upload(default);
            await Wait(() => guest.Room?.Chart?.ShareId is not null);
            var room = guest.Room!; var id = room.Chart!.ShareId!.Value;
            Assert.InRange(room.Chart.ExpiresAt!.Value - DateTime.UtcNow, TimeSpan.FromMinutes(119), TimeSpan.FromMinutes(121));
            Assert.False(Directory.Exists(Path.Combine(guestRoot, "Shared")));
            bool downloadProgress = false, installProgress = false;
            guest.Changed += () => { downloadProgress |= guest.Progress?.Stage == "Downloading" && guest.Progress.Completed > 0; installProgress |= guest.Progress?.Stage == "Installing"; };
            bool cancelledDownload = false;
            Action stopDownload = () => { if (!cancelledDownload && guest.Progress?.Stage == "Downloading" && guest.Progress.Completed > 0) { cancelledDownload = true; guest.CancelTransfer(); } };
            guest.Changed += stopDownload;
            await guest.Download(default); guest.Changed -= stopDownload;
            Assert.True(cancelledDownload); Assert.Null(guest.AvailableChart);
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(guestRoot, "Shared", ".incoming"), "*.part"));
            await guest.Download(default);
            Assert.True(downloadProgress); Assert.True(installProgress); Assert.Equal(imported, guest.AvailableChart);
            Assert.StartsWith(Path.Combine(guestRoot, "Shared") + Path.DirectorySeparatorChar, imported!);
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(imported)!, "another.bms")));
            Assert.Equal(SHA256.HashData(audio), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(imported)!, "tone.wav"))));
            await guest.Ready(default); await Wait(() => host.Room!.Members.Single(m => m.Id == guest.User!.Id).Ready);
            var expiry = room.Chart.ExpiresAt;
            await host.SelectChart(new(Path.Combine(source, "another.bms"), "Shared fixture", "Test", 7, 2), default);
            await Wait(() => host.AvailableChart?.EndsWith("another.bms") == true && guest.AvailableChart?.EndsWith("another.bms") == true);
            await host.Upload(default);
            await Wait(() => guest.Room?.Chart?.ShareId == id);
            Assert.Equal(expiry, guest.Room!.Chart!.ExpiresAt);
            Assert.True(File.Exists(imported));
            using var credentials = await http.PostAsJsonAsync("/api/auth/token", new { username = "other_" + suffix, password });
            var login = (await credentials.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login);
            using (var forbidden = await http.GetAsync($"/api/room-content/{id}/download")) Assert.False(forbidden.IsSuccessStatusCode);
            await using var data = NpgsqlDataSource.Create(connection);
            await using (var expire = data.CreateCommand("UPDATE room_content SET expires_at=now()-interval '1 second' WHERE id=@id")) { expire.Parameters.AddWithValue("id", id); await expire.ExecuteNonQueryAsync(); }
            await Assert.ThrowsAsync<HttpRequestException>(() => guest.Download(default));
            Assert.True(File.Exists(imported));
            var dotnet = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_DOTNET")!;
            var server = Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_SERVER")!;
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(server); start.ArgumentList.Add("--cleanup-content");
            using var cleanup = Process.Start(start)!; await cleanup.WaitForExitAsync(); Assert.Equal(0, cleanup.ExitCode);
            await using var check = data.CreateCommand("SELECT state,deleted_at FROM room_content WHERE id=@id"); check.Parameters.AddWithValue("id", id);
            await using var reader = await check.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal("expired", reader.GetString(0)); Assert.False(reader.IsDBNull(1));
            Assert.False(File.Exists(Path.Combine(Environment.GetEnvironmentVariable("Cloud__StoragePath")!, "rooms", id.ToString("N") + ".zip")));
            Assert.True(File.Exists(imported));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
