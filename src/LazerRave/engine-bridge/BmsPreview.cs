using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LazerRave.Bridge;

internal sealed record PreviewNote(double Time, string Path);
internal sealed record PreviewPlan(string? Cover, string? Track, PreviewNote[] Notes, string? Reason = null)
{
    public static PreviewPlan Read(Chart chart, string encoding)
    {
        var directory = Path.GetDirectoryName(chart.Path)!;
        if (new FileInfo(chart.Path).Length > 16 * 1024 * 1024) return new(null, null, [], "Chart is too large for preview.");
        var bytes = File.ReadAllBytes(chart.Path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(encoding == "gb18030" ? 54936 : 932).GetString(bytes); }
        if (encoding is "cp932" or "gb18030" && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }))
            text = Encoding.GetEncoding(encoding == "gb18030" ? 54936 : 932).GetString(bytes);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n').Select(line => line.Trim().TrimStart('\uFEFF')).ToArray();
        var measures = new Dictionary<int, double>();
        var events = new List<(int Measure, double Fraction, string Channel, string Value)>();
        var randoms = new Stack<int>();
        var branches = new Stack<(bool Parent, bool Taken)>();
        bool active = true;
        foreach (var line in lines)
        {
            if (!line.StartsWith('#')) continue;
            var directive = line[1..].Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (directive.Length == 0) continue;
            string command = directive[0].ToUpperInvariant();
            string argument = directive.Length > 1 ? directive[1].Trim() : "";
            int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number);
            switch (command)
            {
                case "RANDOM": randoms.Push(1); continue;
                case "SETRANDOM": randoms.Push(Math.Max(1, number)); continue;
                case "ENDRANDOM": if (randoms.Count > 0) randoms.Pop(); continue;
                case "IF":
                    bool matchBranch = randoms.Count > 0 && randoms.Peek() == number;
                    branches.Push((active, matchBranch)); active &= matchBranch; continue;
                case "ELSEIF":
                    if (branches.TryPop(out var previous))
                    {
                        bool next = !previous.Taken && randoms.Count > 0 && randoms.Peek() == number;
                        branches.Push((previous.Parent, previous.Taken || next)); active = previous.Parent && next;
                    }
                    continue;
                case "ELSE":
                    if (branches.TryPop(out var otherwise))
                    { branches.Push((otherwise.Parent, true)); active = otherwise.Parent && !otherwise.Taken; }
                    continue;
                case "ENDIF": if (branches.TryPop(out var parent)) active = parent.Parent; continue;
            }
            if (!active) continue;
            var match = Regex.Match(line, @"^#(\d{3})([0-9A-Za-z]{2}):(.+)$");
            if (match.Success)
            {
                int measure = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var channel = match.Groups[2].Value.ToUpperInvariant(); var data = match.Groups[3].Value.Trim();
                if (channel == "02") { if (double.TryParse(data, CultureInfo.InvariantCulture, out var ratio) && ratio is > 0 and <= 64) measures[measure] = ratio; continue; }
                if (data.Length > 8192 || data.Length % 2 != 0) continue;
                for (int i = 0; i < data.Length; i += 2)
                    if (data.Substring(i, 2) != "00") events.Add((measure, (double)i / data.Length, channel, data.Substring(i, 2).ToUpperInvariant()));
            }
            else if (line.StartsWith('#'))
            {
                int space = line.IndexOfAny([' ', '\t']);
                if (space > 1) headers[line[1..space]] = line[(space + 1)..].Trim();
            }
        }
        string? Media(string name, bool audio)
        {
            var path = Path.GetFullPath(name.Replace('/', Path.DirectorySeparatorChar), directory);
            if (!SongLibrary.ContainsPath(directory, path)) return null;
            var extensions = audio ? new[] { ".ogg", ".wav", ".mp3", ".flac" } : new[] { ".png", ".jpg", ".jpeg", ".bmp" };
            if (File.Exists(path) && extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return path;
            foreach (var extension in extensions) { var alternate = Path.ChangeExtension(path, extension); if (File.Exists(alternate)) return alternate; }
            return null;
        }
        var cover = new[] { "STAGEFILE", "BACKBMP", "BANNER" }.Select(key => headers.TryGetValue(key, out var value) ? Media(value, false) : null).FirstOrDefault(path => path is not null);
        var track = headers.TryGetValue("PREVIEW", out var preview) ? Media(preview, true) : Media("preview.ogg", true);
        var beats = new double[1001];
        for (int i = 0; i < 1000; i++) beats[i + 1] = beats[i] + 4 * measures.GetValueOrDefault(i, 1);
        double bpm = headers.TryGetValue("BPM", out var initial) && double.TryParse(initial, CultureInfo.InvariantCulture, out var initialBpm) && initialBpm > 0
            ? initialBpm : chart.Bpm > 0 ? chart.Bpm : 120, seconds = 0, previousBeat = 0;
        var notes = new List<PreviewNote>(); var longNotes = new HashSet<string>();
        foreach (var item in events.OrderBy(item => beats[item.Measure] + 4 * measures.GetValueOrDefault(item.Measure, 1) * item.Fraction)
            .ThenBy(item => item.Channel is "03" or "08" ? 0 : item.Channel == "09" ? 2 : 1))
        {
            double beat = beats[item.Measure] + 4 * measures.GetValueOrDefault(item.Measure, 1) * item.Fraction;
            seconds += (beat - previousBeat) * 60 / bpm; previousBeat = beat;
            if (item.Channel == "03") { if (int.TryParse(item.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n > 0) bpm = n; continue; }
            if (item.Channel == "08") { if (headers.TryGetValue("BPM" + item.Value, out var value) && double.TryParse(value, CultureInfo.InvariantCulture, out var n) && n > 0) bpm = n; continue; }
            if (item.Channel == "09") { if (headers.TryGetValue("STOP" + item.Value, out var value) && double.TryParse(value, CultureInfo.InvariantCulture, out var n) && n >= 0) seconds += n / 48 * 60 / bpm; continue; }
            bool key = item.Channel.Length == 2 && "1256".Contains(item.Channel[0]) && "12345689".Contains(item.Channel[1]);
            if (item.Channel != "01" && !key) continue;
            if (key && headers.TryGetValue("LNOBJ", out var endNote) && item.Value.Equals(endNote, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.Channel[0] is '5' or '6') { if (!longNotes.Add(item.Channel)) { longNotes.Remove(item.Channel); continue; } }
            if (headers.TryGetValue("WAV" + item.Value, out var file) && Media(file, true) is { } sound) notes.Add(new(seconds, sound));
            if (notes.Count >= 50000) break;
        }
        var start = notes.FirstOrDefault()?.Time ?? 0;
        return notes.Count > 0
            ? new(cover, null, notes.Select(note => note with { Time = note.Time - start }).ToArray())
            : new(cover, track, [], track is null ? "No preview audio found." : null);
    }
}
