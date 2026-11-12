using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace QBounce;

// Only three small NAudio assemblies, Windows codecs, and WinMM output. No external player/process.
internal sealed class NAudioPlayback : IAudioPlayback
{
    private readonly WaveStream reader;
    private readonly WaveOutEvent output;
    private readonly LoopStream loop;
    private readonly VolumeSampleProvider volume;
    public Exception? Error { get; private set; }
    internal PlaybackState State => output.PlaybackState;
    public float Volume { set => volume.Volume = value; }

    public NAudioPlayback(string path)
    {
        reader = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? new WaveFileReader(path) : new MediaFoundationReader(path);
        output = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 };
        loop = new LoopStream(reader);
        output.PlaybackStopped += (_, args) => { if (args.Exception != null) Error = args.Exception; };
        try
        {
            volume = new VolumeSampleProvider(loop.ToSampleProvider());
            output.Init(volume.ToWaveProvider());
        }
        catch { loop.Dispose(); try { output.Dispose(); } finally { reader.Dispose(); } throw; }
    }

    public void Play() => output.Play();
    public void Stop() => output.Stop();
    public void Dispose()
    {
        // WaveOutEvent.Stop does not join its worker. Close the read gate first so a late
        // callback cannot recreate MediaFoundationReader's native reader after disposal.
        loop.Dispose();
        try { output.Dispose(); }
        finally { reader.Dispose(); }
    }
}

public sealed class LoopStream(WaveStream source) : WaveStream
{
    private readonly object readGate = new();
    private bool closed;
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (readGate) return closed ? 0 : ReadOpen(buffer, offset, count);
    }
    private int ReadOpen(byte[] buffer, int offset, int count)
    {
        var total = 0;
        var rewoundWithoutData = false;
        while (total < count)
        {
            var read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (rewoundWithoutData) break; // empty/corrupt streams must never spin forever
                source.Position = 0;
                rewoundWithoutData = true;
                continue;
            }
            rewoundWithoutData = false;
            total += read;
        }
        return total;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) lock (readGate) closed = true;
        base.Dispose(disposing);
    }
    // The owner owns source; closing this adapter prevents further reads without disposing it.
}
