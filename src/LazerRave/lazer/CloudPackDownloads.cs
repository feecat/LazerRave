using System.Diagnostics;
using System.Net.Http.Json;
using LazerRave.Content;

namespace LazerRave.Lazer;

internal sealed partial class CloudClient
{
    private sealed record PackDetails(PackMetadata Pack);
    private sealed record PackMetadata(string Sha256, long SizeBytes);

    internal static Guid PackId(string link, Uri server)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != server.GetLeftPart(UriPartial.Authority)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Use a song pack link from the configured LazerRave website.");
        var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (parts.Length == 3 && parts[1] == "packs" && Guid.TryParse(parts[2], out var page)) return page;
        if (parts.Length == 5 && parts[1] == "api" && parts[2] == "packs" && parts[4] == "download" && Guid.TryParse(parts[3], out var download)) return download;
        throw new ArgumentException("Use a song pack page or ZIP download link.");
    }

    public Task DownloadPack(string link, CancellationToken cancellation) => RunTransfer(async ct =>
    {
        var server = Server ?? ServerUri("https://lazerrave.com");
        var id = PackId(link, server);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = server, Timeout = TimeSpan.FromMinutes(10) };
        using var metadataResponse = await client.GetAsync($"api/packs/{id}", ct);
        await Check(metadataResponse, ct);
        var metadata = (await metadataResponse.Content.ReadFromJsonAsync<PackDetails>(json, ct))?.Pack;
        if (metadata is null || !SongContent.IsHash(metadata.Sha256) || metadata.SizeBytes is <= 0 or > SongContent.MaxArchiveBytes)
            throw new InvalidDataException("The song pack metadata is invalid or exceeds the download limit.");
        LibraryFolders.Ensure(applicationRoot);
        var incoming = Path.Combine(SharedRoot, ".incoming");
        Directory.CreateDirectory(incoming); LibraryFolders.NoLink(incoming);
        var zip = Path.Combine(incoming, "pack-" + Guid.NewGuid().ToString("N") + ".zip.part");
        try
        {
            using var response = await client.GetAsync($"api/packs/{id}/download", HttpCompletionOption.ResponseHeadersRead, ct);
            await Check(response, ct);
            if (response.Content.Headers.ContentLength is { } length && length != metadata.SizeBytes)
                throw new InvalidDataException("Downloaded ZIP size does not match the pack metadata.");
            var clock = Stopwatch.StartNew();
            SetProgress(new("Downloading", 0, metadata.SizeBytes));
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    if (output.Position + read > metadata.SizeBytes) throw new InvalidDataException("Downloaded ZIP exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    SetProgress(new("Downloading", output.Position, metadata.SizeBytes, output.Position / Math.Max(.01, clock.Elapsed.TotalSeconds)));
                }
                if (output.Length != metadata.SizeBytes) throw new IOException("Song pack download was interrupted. Try again.");
            }
            SetProgress(new("Verifying ZIP", 0, 1));
            if (await SongContent.HashFile(zip, ct) != metadata.Sha256) throw new InvalidDataException("Song pack ZIP checksum failed.");
            await InstallPack(zip, ct);
        }
        finally { if (File.Exists(zip)) File.Delete(zip); }
    }, cancellation);

    public Task ImportPack(string zip, CancellationToken cancellation) => RunTransfer(async ct =>
    {
        if (!Path.GetExtension(zip).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Select a ZIP song pack.");
        await InstallPack(Path.GetFullPath(zip), ct);
    }, cancellation);

    private async Task InstallPack(string zip, CancellationToken cancellation)
    {
        var directory = await Task.Run(() => SongPackFiles.Install(zip, applicationRoot, cancellation, new CallbackProgress<TransferProgress>(SetProgress)), cancellation);
        SetProgress(new("Scanning…", 0, 1));
        await installed(directory);
        Status = "Installed to BMS/Shared"; Progress = null; Notify();
    }
}
