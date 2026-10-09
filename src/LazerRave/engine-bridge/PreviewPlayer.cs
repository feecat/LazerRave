using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;

namespace LazerRave.Bridge;

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
    public void Start(PreviewPlan plan, double clock)
    {
        Stop(); start = clock; notes = plan.Notes;
        if (plan.Track is not null) { track = trackStore.Get(plan.Track); if (track is not null) { track.Looping = true; track.Start(); } }
        else
        {
            foreach (var path in notes.Select(note => note.Path).Distinct())
            {
                if (!samples.ContainsKey(path)) { var sample = sampleStore.Get(path); if (sample is not null) samples[path] = sample; }
            }
        }
        Playing = track is not null || notes.Length > 0;
    }
    public void Update(double clock)
    {
        if (!Playing || track is not null) return;
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
        if (elapsed > 26) { foreach (var voice in voices) voice.Channel.Stop(); voices.Clear(); next = 0; start = clock; }
    }
    public void Stop()
    {
        track?.Stop(); track?.Dispose(); track = null;
        foreach (var voice in voices) voice.Channel.Stop(); voices.Clear(); notes = []; next = 0; Playing = false;
        foreach (var sample in samples.Values) sample.Dispose(); samples.Clear();
    }
    public void Dispose() { Stop(); sampleStore.Dispose(); trackStore.Dispose(); }
}
