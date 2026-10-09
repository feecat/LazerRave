using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LazerRave.Bridge;

internal sealed record BmsTempo(double Time, double Bpm);
internal sealed record BmsTimeline(double Length, BmsTempo[] Tempos)
{
    public static BmsTimeline Read(Chart chart, string encoding)
    {
        if (new FileInfo(chart.Path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Chart is too large to read timing metadata.");
        var bytes = File.ReadAllBytes(chart.Path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(encoding == "gb18030" ? 54936 : 932).GetString(bytes); }
        if (encoding is "cp932" or "gb18030" && !bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }))
            text = Encoding.GetEncoding(encoding == "gb18030" ? 54936 : 932).GetString(bytes);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var measures = new Dictionary<int, double>();
        var events = new List<(int Measure, double Fraction, string Channel, string Value)>();
        var randoms = new Stack<int>();
        var branches = new Stack<(bool Parent, bool Taken)>();
        bool active = true;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (!line.StartsWith('#')) continue;
            var directive = line[1..].Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (directive.Length == 0) continue;
            string command = directive[0].ToUpperInvariant();
            string argument = directive.Length > 1 ? directive[1].Trim() : "";
            int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number);
            switch (command)
            {
                // Library metadata uses a stable branch; an actual play can choose another random seed.
                case "RANDOM": randoms.Push(1); continue;
                case "SETRANDOM": randoms.Push(Math.Max(1, number)); continue;
                case "ENDRANDOM": if (randoms.Count > 0) randoms.Pop(); continue;
                case "IF":
                    bool match = randoms.Count > 0 && randoms.Peek() == number;
                    branches.Push((active, match)); active &= match; continue;
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
            var row = Regex.Match(line, @"^#(\d{3})([0-9A-Za-z]{2}):(.+)$");
            if (!row.Success) { if (argument.Length > 0) headers[command] = argument; continue; }
            int measure = int.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture);
            string channel = row.Groups[2].Value.ToUpperInvariant(), data = row.Groups[3].Value.Trim();
            if (channel == "02")
            {
                if (Number(data) is > 0 and <= 64) measures[measure] = Number(data);
                continue;
            }
            if (data.Length > 8192 || data.Length % 2 != 0) continue;
            for (int index = 0; index < data.Length; index += 2)
            {
                string value = data.Substring(index, 2).ToUpperInvariant();
                if (value != "00") events.Add((measure, (double)index / data.Length, channel, value));
            }
        }
        var beats = new double[1001];
        for (int measure = 0; measure < 1000; measure++) beats[measure + 1] = beats[measure] + 4 * measures.GetValueOrDefault(measure, 1);
        double bpm = headers.TryGetValue("BPM", out var initial) && Number(initial) > 0 ? Number(initial) : chart.Bpm > 0 ? chart.Bpm : 120;
        double seconds = 0, lastBeat = 0, end = 0;
        var tempos = new List<BmsTempo> { new(0, bpm) };
        foreach (var item in events.OrderBy(item => beats[item.Measure] + 4 * measures.GetValueOrDefault(item.Measure, 1) * item.Fraction)
            .ThenBy(item => item.Channel is "03" or "08" ? 0 : item.Channel == "09" ? 2 : 1))
        {
            double beat = beats[item.Measure] + 4 * measures.GetValueOrDefault(item.Measure, 1) * item.Fraction;
            seconds += (beat - lastBeat) * 60 / bpm; lastBeat = beat;
            if (item.Channel is "03" or "08")
            {
                double next = item.Channel == "03" ? int.TryParse(item.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int direct) ? direct : 0
                    : headers.TryGetValue("BPM" + item.Value, out var extended) ? Number(extended) : 0;
                if (next > 0) { bpm = next; tempos.Add(new(seconds * 1000, bpm)); }
            }
            else if (item.Channel == "09")
            {
                if (headers.TryGetValue("STOP" + item.Value, out var stop) && Number(stop) >= 0) seconds += Number(stop) / 48 * 60 / bpm;
            }
            else if (item.Channel is "01" or "04" or "06" or "07" ||
                "1256DE".Contains(item.Channel[0]) && item.Channel[1] is >= '1' and <= '9')
                end = Math.Max(end, seconds * 1000);
        }
        return new(end, tempos.ToArray());
    }

    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && double.IsFinite(result) ? result : 0;
}
