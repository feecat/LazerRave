using System.Diagnostics;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LazerRave.Bridge;

internal sealed class EngineBridge(string runtime)
{
    public string Runtime { get; } = Path.GetFullPath(runtime);
    public string Executable => Path.Combine(Runtime, "OpenLR2_x64.exe");
    public async Task RunClassic(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = Process.Start(ClassicLaunch.Create(Runtime)) ?? throw new IOException("Cannot start OpenLR2.");
        using var ownership = new OwnedEngineJob(process);
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        if (process.ExitCode != 0) throw new IOException($"OpenLR2 exited with code {process.ExitCode}.");
    }
    public static XDocument Request(string mode, FrontendSettings settings, Chart? chart = null, EmbedTarget? embedding = null)
    {
        var root = new XElement("lazerrave", new XAttribute("version", "1"), new XAttribute("mode", mode), new XElement("encoding", settings.Encoding));
        if (mode is "sync" or "catalog" or "import")
        {
            root.Add(new XElement("library-source", "settings"));
            foreach (var directory in ApplicationPaths.LibraryRoots(settings.Roots))
            {
                if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                root.Add(new XElement("root", Path.GetFullPath(directory)));
            }
        }
        if (mode == "import") root.Add(new XElement("chart", chart?.Path ?? throw new ArgumentException("Select an installed chart.")));
        if (mode is "play" or "validate")
        {
            if (chart is null || !File.Exists(chart.Path) || !new[] { ".bms", ".bme", ".bml", ".pms" }.Contains(Path.GetExtension(chart.Path).ToLowerInvariant()))
                throw new FileNotFoundException("Select an available BMS chart.");
            root.Add(new XElement("chart", chart.Path), new XElement("speed", Math.Round(settings.Speed * 100).ToString(CultureInfo.InvariantCulture)),
                new XElement("offset", settings.Offset), new XElement("arrangement", Array.IndexOf(PlayOptionCatalog.Arrangements, settings.Arrangement)));
            PlayOptionCatalog.Validate(settings.PlayOptions);
            root.Add(new XElement("play-options", PlayOptionCatalog.All.Select(option =>
                new XElement("option", new XAttribute("name", option.Name), new XAttribute("value", PlayOptionCatalog.Get(settings, option))))));
        }
        if (embedding is not null)
        {
            if (embedding.Window == 0 || embedding.Process == 0 || mode is not ("play" or "validate" or "embed-probe"))
                throw new ArgumentException("Invalid embedded launch target.");
            root.Add(new XElement("embed-window", embedding.Window), new XElement("host-process", embedding.Process),
                new XElement("frame-limit", settings.FrameLimit), new XElement("render-profile", settings.RenderProfile));
        }
        return new(root);
    }
    public async Task<XDocument> Exchange(string mode, FrontendSettings settings, Chart? chart = null, CancellationToken cancellation = default,
        EmbedTarget? embedding = null, Action<IntPtr, int>? ready = null, Action<GameplaySnapshot>? progress = null, string? replaySource = null, string? replayDestination = null)
    {
        if (!File.Exists(Executable)) throw new FileNotFoundException("OpenLR2_x64.exe is missing from the application folder.");
        var capabilities = System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Executable, cancellation));
        if (!capabilities.Contains("LAZERRAVE_BRIDGE_V1", StringComparison.Ordinal))
            throw new InvalidDataException("OpenLR2 does not support the LazerRave bridge.");
        if (embedding is not null && !capabilities.Contains("LAZERRAVE_EMBED_V1", StringComparison.Ordinal))
            throw new InvalidDataException("Rebuild OpenLR2 before using embedded play.");
        if (mode is "play" or "validate" && !capabilities.Contains("LAZERRAVE_PLAY_OPTIONS_V1", StringComparison.Ordinal))
            throw new InvalidDataException("Rebuild OpenLR2 before using gameplay options.");
        if (mode == "play" && progress is not null && !capabilities.Contains("LAZERRAVE_SCORE_STREAM_V1", StringComparison.Ordinal))
            throw new InvalidDataException("Rebuild OpenLR2 before using multiplayer scores.");
        var directory = Path.Combine(Runtime, "cache", "engine-requests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var request = Path.Combine(directory, "request.xml");
            var document = Request(mode, settings, chart, embedding);
            if (mode == "play" && progress is not null) document.Root!.Add(new XElement("telemetry", "LAZERRAVE_SCORE_STREAM_V1"));
            if (replaySource is not null)
            {
                var snapshot = Path.Combine(directory, "playback.lr2rep");
                File.Copy(replaySource, snapshot, false);
                ReplayFile.Validate(snapshot);
                document.Root!.Add(new XElement("replay", snapshot));
            }
            document.Save(request);
            var start = new ProcessStartInfo(Executable) { WorkingDirectory = Runtime, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--lazerrave-request"); start.ArgumentList.Add(request);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start OpenLR2.");
            using var ownership = mode is "play" or "embed-probe" ? new OwnedEngineJob(process) : null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            if (mode != "play") timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var scoreStop = new CancellationTokenSource();
            var scores = mode == "play" && progress is not null ? WatchScores(request + ".progress.xml", progress, scoreStop.Token) : Task.CompletedTask;
            try
            {
                if (mode is "play" or "embed-probe" && embedding is not null)
                    await WaitForEmbeddedReady(process, request, embedding, ready, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
            }
            catch { await StopOwnedProcess(process, request, mode is "play" or "embed-probe"); throw; }
            finally
            {
                scoreStop.Cancel(); await scores;
                if (progress is not null && File.Exists(request + ".progress.xml")) progress(GameplaySnapshot.Read(request + ".progress.xml"));
            }
            if (mode == "play")
            {
                if (process.ExitCode != 0) throw new IOException($"OpenLR2 exited with code {process.ExitCode}.");
                var finalReply = request + ".reply.xml";
                if (embedding is not null && File.Exists(finalReply)) EnsureOk(ReadReply(finalReply));
                return new(new XElement("lazerrave", new XAttribute("status", "ok")));
            }
            var reply = request + ".reply.xml";
            var replyDocument = ReadReply(reply);
            if (process.ExitCode != 0 || (string?)replyDocument.Root?.Attribute("status") != "ok")
                throw new IOException((string?)replyDocument.Root?.Element("message") ?? $"OpenLR2 exited with code {process.ExitCode}.");
            return replyDocument;
        }
        finally
        {
            try
            {
                var replay = Path.Combine(directory, "request.xml.replay.lr2rep");
                if (replayDestination is not null && File.Exists(replay))
                {
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(replayDestination)!);
                    ReplayFile.Validate(replay);
                    string temporary = replayDestination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.Copy(replay, temporary, false); File.Move(temporary, replayDestination, false); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            finally { System.IO.Directory.Delete(directory, true); }
        }
    }
    private static async Task WatchScores(string path, Action<GameplaySnapshot> callback, CancellationToken cancellation)
    {
        GameplaySnapshot? previous = null;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(120));
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                if (!File.Exists(path)) continue;
                GameplaySnapshot value;
                try { value = GameplaySnapshot.Read(path); }
                catch (Exception error) when (error is IOException or XmlException) { continue; }
                if (value != previous) { previous = value; callback(value); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    public async Task<SongLibrary> Catalog(FrontendSettings settings, bool sync, CancellationToken cancellation = default) =>
        SongLibrary.Parse(await Exchange(sync ? "sync" : "catalog", settings, cancellation: cancellation), Runtime, ApplicationPaths.LibraryRoots(settings.Roots));
    public async Task<SongLibrary> Import(FrontendSettings settings, string path, CancellationToken cancellation) =>
        SongLibrary.Parse(await Exchange("import", settings, new Chart(path, "", "", 7, 0, 0, 120, 0, null), cancellation), Runtime, ApplicationPaths.LibraryRoots(settings.Roots));
    private static XDocument ReadReply(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 64 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static void EnsureOk(XDocument reply)
    {
        if (reply.Root?.Attribute("status")?.Value != "ok") throw new IOException(reply.Root?.Element("message")?.Value ?? "Embedded startup failed.");
    }
    private static async Task WaitForEmbeddedReady(Process process, string request, EmbedTarget target, Action<IntPtr, int>? ready, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        while (!process.HasExited)
        {
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(request + ".reply.xml"))
            {
                var reply = ReadReply(request + ".reply.xml"); EnsureOk(reply);
                if (!ulong.TryParse(reply.Root?.Element("engine-window")?.Value, out var handle) || handle == 0 ||
                    !ulong.TryParse(reply.Root?.Element("viewport")?.Value, out var viewport) || viewport != target.Window)
                    throw new IOException("OpenLR2 returned an invalid embedding acknowledgement.");
                var window = new IntPtr(unchecked((long)handle));
                if (!NativeWindowApi.IsWindow(window) || NativeWindowApi.GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)process.Id)
                    throw new IOException("The acknowledged game window is unavailable.");
                ready?.Invoke(window, process.Id); return;
            }
            if (clock.Elapsed > TimeSpan.FromSeconds(120)) throw new TimeoutException("OpenLR2 did not connect to the game viewport.");
            await Task.Delay(50, cancellation);
        }
        if (File.Exists(request + ".reply.xml")) EnsureOk(ReadReply(request + ".reply.xml"));
        throw new IOException($"OpenLR2 exited before connecting to the viewport ({process.ExitCode}).");
    }
    private static async Task StopOwnedProcess(Process process, string request, bool graceful)
    {
        if (process.HasExited) return;
        if (graceful)
        {
            File.WriteAllText(request + ".stop", "stop");
            var replyFile = request + ".reply.xml";
            if (File.Exists(replyFile))
            {
                try
                {
                    var reply = ReadReply(replyFile);
                    if (ulong.TryParse(reply.Root?.Element("engine-window")?.Value, out var value))
                    {
                        var window = new IntPtr(unchecked((long)value));
                        if (NativeWindowApi.GetWindowThreadProcessId(window, out uint owner) != 0 && owner == (uint)process.Id)
                            NativeWindowApi.PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                catch (Exception error) when (error is IOException or XmlException) { }
            }
            process.Refresh();
            var mainWindow = process.MainWindowHandle;
            if (mainWindow != IntPtr.Zero && NativeWindowApi.GetWindowThreadProcessId(mainWindow, out uint mainOwner) != 0 && mainOwner == (uint)process.Id)
                NativeWindowApi.PostMessageW(mainWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(deadline.Token); return; } catch (OperationCanceledException) { }
        }
        if (!process.HasExited) process.Kill(true);
        await process.WaitForExitAsync(CancellationToken.None);
    }
    public async Task Play(FrontendSettings settings, Chart chart, CancellationToken cancellation, EmbedTarget? embedding = null, Action<IntPtr, int>? ready = null, Action<GameplaySnapshot>? progress = null, string? replaySource = null, string? replayDestination = null)
    {
        await Exchange("validate", settings, chart, cancellation, embedding, replaySource: replaySource);
        if (embedding is not null) { await Exchange("play", settings, chart, cancellation, embedding, ready, progress, replaySource, replayDestination); return; }
        var start = new ProcessStartInfo("powershell.exe") { WorkingDirectory = Runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Runtime, "set-window.ps1"),
            "-RuntimeDirectory", Runtime, "-Width", settings.Width.ToString(CultureInfo.InvariantCulture), "-Height", settings.Height.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(value);
        using var configure = Process.Start(start) ?? throw new IOException("Cannot configure the game window.");
        var error = configure.StandardError.ReadToEndAsync(cancellation);
        await configure.WaitForExitAsync(cancellation);
        if (configure.ExitCode != 0) throw new IOException(await error);
        await Exchange("play", settings, chart, cancellation, progress: progress, replaySource: replaySource, replayDestination: replayDestination);
    }
}
