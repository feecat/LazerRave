using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;

namespace LazerRave.Bridge;

internal sealed class PreparedPreview(PreviewNote[] notes) : IDisposable
{
    public PreviewNote[] Notes { get; } = notes;
    public Track? Track;
    public Dictionary<string, Sample> Samples { get; } = new(StringComparer.OrdinalIgnoreCase);
    public void Dispose()
    {
        Track?.Dispose(); Track = null;
        foreach (var sample in Samples.Values) sample.Dispose();
        Samples.Clear();
    }
}

internal sealed class PreviewPlayer(AudioManager audio, ResourceStore<byte[]> resources) : IDisposable
{
    private readonly ISampleStore sampleStore = audio.GetSampleStore(resources);
    private readonly ITrackStore trackStore = audio.GetTrackStore(resources);
    private readonly Dictionary<string, Sample> samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(SampleChannel Channel, double Started)> voices = [];
    private PreviewNote[] notes = [];
    private Track? track;
    private double start;
    private int next;
    public bool Playing { get; private set; }
    public PreparedPreview Prepare(PreviewPlan plan, CancellationToken cancellation)
    {
        var prepared = new PreparedPreview(plan.Notes);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (plan.Track is not null) prepared.Track = trackStore.Get(plan.Track);
            else
            {
                foreach (var path in plan.Notes.Select(note => note.Path).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (sampleStore.Get(path) is { } sample) prepared.Samples[path] = sample;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }
    public void Start(PreparedPreview prepared, double clock)
    {
        Stop(); start = clock; notes = prepared.Notes;
        track = prepared.Track; prepared.Track = null;
        foreach (var sample in prepared.Samples) samples.Add(sample.Key, sample.Value);
        prepared.Samples.Clear();
        if (track is not null) { track.Looping = false; track.Start(); }
        Playing = track is not null || samples.Count > 0;
    }
    public void Update(double clock)
    {
        if (!Playing) return;
        if (track is not null)
        {
            if (track.HasCompleted) Stop();
            return;
        }
        double elapsed = (clock - start) / 1000;
        while (next < notes.Length && notes[next].Time <= elapsed)
        {
            if (samples.TryGetValue(notes[next].Path, out var sample))
            {
                var channel = sample.GetChannel(); channel.Play(); voices.Add((channel, clock));
            }
            next++;
        }
        voices.RemoveAll(voice => clock - voice.Started > 250 && !voice.Channel.Playing);
        if (next == notes.Length && voices.Count == 0 && elapsed > notes[^1].Time + 0.5) Stop();
    }
    public void Stop()
    {
        track?.Stop(); track?.Dispose(); track = null;
        foreach (var voice in voices) voice.Channel.Stop(); voices.Clear(); notes = []; next = 0; Playing = false;
        foreach (var sample in samples.Values) sample.Dispose(); samples.Clear();
    }
    public void Dispose() { Stop(); sampleStore.Dispose(); trackStore.Dispose(); }
}
