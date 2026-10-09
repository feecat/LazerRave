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
    public LoopbackFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LAZERRAVE_CLOUD_TEST_URL") is null) Skip = "Requires the isolated loopback cloud test server.";
    }
}

public sealed class CloudClientTransferTests
{
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
