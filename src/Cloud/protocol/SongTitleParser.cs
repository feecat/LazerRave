using System.Text.RegularExpressions;

namespace LazerRave.Content;

public sealed record SongChart(Guid Id, string Title, string Artist, string Difficulty, int Keys,
    string Scope = "community", Guid[]? Packs = null, string? Subtitle = null);
public sealed record SongClassification(Guid Id, string Title, string Artist, string Difficulty);

public static class SongTitleParser
{
    private static readonly Regex whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex suffix = new(@"^(?<base>.+?)\s*(?:\[(?<square>[^\[\]]+)\]|\((?<round>[^()]+)\)|［(?<wide>[^［］]+)］|（(?<wideRound>[^（）]+)）|<(?<angle>[^<>]+)>|-(?<dash>[^-]+)-)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex version = new(@"\b(remix|mix|edit|version|ver|remaster|remastered|original|instrumental|extended|short|long|cut|arrange|arrangement|cover|radio)\b|\bv\.?\s*\d+(?:\.\d+)*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex mode = new(@"^(?:SP|DP|SINGLE|DOUBLE|(?:14|10|9|7|5)\s*(?:KEYS?|K)?)(?:[\s_/:|\-]+|(?=[A-Za-z])|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex abbreviation = new(@"^([BNHAI])(5|7|9|10|14)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex trailingMode = new(@"[\s_/:|\-]+(?:SP|DP|SINGLE|DOUBLE|(?:14|10|9|7|5)\s*(?:KEYS?|K))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex standard = new(@"^(BEGINNER|NORMAL|HYPER|ANOTHER|INSANE|BLACK|LIGHT|EASY|HARD|EXTRA|EX|BASIC|STANDARD|MANIAC|LUNATIC|LEGENDARIA)(?:[\s+_0-9\-]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex plainSuffix = new(@"^(?<base>.+?)\s+(?<plain>(?:(?:SP|DP)\s+)?(?:BEGINNER|NORMAL|HYPER|ANOTHER|INSANE|LIGHT|EASY|HARD|EXTRA|MANIAC|LEGENDARIA)|(?:5|7|9|10|14)\s*(?:KEYS?|K)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex chartCredit = new(@"(?:\s*[/()]\s*|\s+)(?:obj\.?|chart|notes\.?|bga|chara)\b\s*[:.]?\s*.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record Part(string Base, string Label, bool Strong);
    private sealed record Parsed(SongChart Chart, string Artist, string Original, List<Part> Parts)
    {
        public string Root => Parts.LastOrDefault()?.Base ?? Original;
    }

    public static string Normalize(string value) => whitespace.Replace(value.Trim(), " ");
    public static string FullTitle(string title, string? subtitle)
    {
        title = Normalize(title);
        subtitle = Normalize(subtitle ?? "");
        return subtitle.Length == 0 || title.EndsWith(subtitle, StringComparison.OrdinalIgnoreCase)
            ? title : $"{title} {subtitle}".Trim();
    }
    private static string Identity(string value) => Normalize(value).ToUpperInvariant();
    private static string Code(string value) => value switch
    {
        "1" or "B" => "BEGINNER", "2" or "N" => "NORMAL", "3" or "H" => "HYPER",
        "4" or "A" => "ANOTHER", "5" or "I" => "INSANE", _ => value,
    };

    private static string Label(string value, int keys, out bool hasMode)
    {
        value = Normalize(value.Trim(' ', '[', ']', '(', ')', '［', '］', '（', '）', '【', '】', '<', '>', '-', '"', '～'));
        var abbreviated = abbreviation.Match(value);
        if (abbreviated.Success) { hasMode = true; return Code(abbreviated.Groups[1].Value.ToUpperInvariant()); }
        var prefix = mode.Match(value);
        hasMode = prefix.Success;
        if (hasMode) value = value[prefix.Length..].Trim(' ', '_', '/', ':', '|', '-');
        var ending = trailingMode.Match(value);
        if (ending.Success) { hasMode = true; value = value[..ending.Index]; }
        // A key suffix such as express14 is meaningful only when it matches the chart's key mode.
        if (value.Length > keys.ToString().Length && value.EndsWith(keys.ToString(), StringComparison.Ordinal) &&
            char.IsLetter(value[value.Length - keys.ToString().Length - 1]))
            value = value[..^keys.ToString().Length];
        return Code(Normalize(value).ToUpperInvariant());
    }

    private static Parsed Parse(SongChart chart)
    {
        string original = FullTitle(chart.Title, chart.Subtitle), current = original;
        var parts = new List<Part>();
        string subtitle = Normalize(chart.Subtitle ?? "");
        if (subtitle.Length > 0 && !version.IsMatch(subtitle) && current != Normalize(chart.Title)
            && !suffix.IsMatch(current) && !plainSuffix.IsMatch(current))
        {
            string label = Label(subtitle, chart.Keys, out bool hasMode);
            current = Normalize(chart.Title);
            parts.Add(new(current, label, hasMode || standard.IsMatch(label)));
        }
        for (int depth = 0; depth < 8; depth++)
        {
            var match = suffix.Match(current);
            bool plain = !match.Success;
            if (plain) match = plainSuffix.Match(current);
            if (!match.Success) break;
            string text = new[] { "square", "round", "wide", "wideRound", "angle", "dash", "plain" }
                .Select(name => match.Groups[name].Value).First(value => value.Length > 0);
            if (version.IsMatch(text)) break;
            string label = Label(text, chart.Keys, out bool hasMode);
            string title = Normalize(match.Groups["base"].Value);
            if (title.Length == 0) break;
            bool bareNumber = plain && int.TryParse(text, out _);
            parts.Add(new(title, label, !bareNumber && (hasMode || standard.IsMatch(label))));
            current = title;
        }
        return new(chart, Normalize(chartCredit.Replace(chart.Artist, "")), original, parts);
    }

    public static IReadOnlyList<SongClassification> Classify(IReadOnlyList<SongChart> charts)
    {
        var parsed = charts.Select(Parse).ToArray();
        var sources = parsed.SelectMany(p => (p.Chart.Packs ?? []).Select(pack => (pack, p)))
            .GroupBy(pair => (pair.pack, pair.p.Chart.Scope, Root: Identity(pair.p.Root)))
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.p).ToArray());
        var artists = parsed.ToDictionary(p => p.Chart.Id, p => p.Artist);
        foreach (var p in parsed)
        {
            // Bare / credits are ambiguous. Treat them as chart credits only with same-pack evidence.
            var peers = (p.Chart.Packs ?? []).SelectMany(pack => sources[(pack, p.Chart.Scope, Identity(p.Root))]);
            var baseArtist = peers.Where(peer => peer.Artist.Length > 0 && p.Artist.StartsWith(peer.Artist + " / ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(peer => peer.Artist.Length).FirstOrDefault();
            if (p.Parts.Count > 0 && baseArtist is not null) artists[p.Chart.Id] = baseArtist.Artist;
        }
        var labels = parsed.SelectMany(p => p.Parts.Select(part => (p.Chart.Scope, Artist: Identity(artists[p.Chart.Id]), Base: Identity(part.Base), part.Label)))
            .GroupBy(part => (part.Scope, part.Artist, part.Base))
            .ToDictionary(group => group.Key, group => group.Select(part => part.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var anchors = parsed.Select(p => (p.Chart.Scope, Artist: Identity(artists[p.Chart.Id]), Title: Identity(p.Original)))
            .ToHashSet();
        foreach (var p in parsed)
            foreach (var part in p.Parts.TakeWhile(part => part.Strong))
                anchors.Add((p.Chart.Scope, Identity(artists[p.Chart.Id]), Identity(part.Base)));
        var result = new List<SongClassification>(charts.Count);
        var subtitleFamilies = parsed.GroupBy(p => (p.Chart.Scope, Artist: Identity(p.Artist), Title: Identity(p.Chart.Title)))
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var p in parsed)
        {
            string artist = artists[p.Chart.Id], title = p.Original, label = "";
            string rawMetadata = Normalize(p.Chart.Difficulty);
            string metadata = rawMetadata is "1" or "2" or "3" or "4" or "5" ? Code(rawMetadata) : Label(rawMetadata, p.Chart.Keys, out _);
            string subtitle = Normalize(p.Chart.Subtitle ?? "");
            var siblings = subtitleFamilies[(p.Chart.Scope, Identity(p.Artist), Identity(p.Chart.Title))];
            string subtitleLabel = Label(subtitle, p.Chart.Keys, out bool subtitleHasMode);
            bool repeatedSongSubtitle = siblings.Length > 1 && !subtitleHasMode && !standard.IsMatch(subtitleLabel)
                && siblings.All(sibling => Normalize(sibling.Chart.Subtitle ?? "") == subtitle);
            bool namedSubtitle = subtitle.Length > 0 && !version.IsMatch(subtitle) && !repeatedSongSubtitle;
            if (namedSubtitle)
            {
                if (subtitleLabel.Length > 0)
                {
                    metadata = subtitleLabel;
                    if (p.Parts.Count == 0) title = Normalize(p.Chart.Title);
                }
            }
            foreach (var part in p.Parts)
            {
                var family = (p.Chart.Scope, Identity(artist), Identity(part.Base));
                bool varied = labels[family] > 1;
                bool anchor = anchors.Contains(family);
                if (!part.Strong && !varied && !anchor && (part.Label.Length == 0 || metadata != part.Label)) break;
                title = part.Base;
                if (part.Label.Length > 0) label = part.Label;
            }
            if (namedSubtitle && subtitleLabel.Length > 0) label = subtitleLabel;
            if (label.Length == 0) label = Code(metadata);
            result.Add(new(p.Chart.Id, title, artist, label.Length > 0 ? label : "UNKNOWN"));
        }
        return result;
    }
}
