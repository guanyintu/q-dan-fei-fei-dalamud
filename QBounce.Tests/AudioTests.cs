using NAudio.Wave;
using Xunit;

namespace QBounce.Tests;

public sealed class AudioTests
{
    [Fact]
    public void MusicSelectionHonorsUserChoiceThenConfigDefaultThenBundledDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "qbounce-paths-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var config = Path.Combine(folder, "config-default.mp3");
        var bundled = Path.Combine(folder, "bundled-default.mp3");
        var selected = Path.Combine(folder, "my-track.wav");
        try
        {
            Assert.Equal(config, AudioService.ResolvePath("", config, bundled));
            File.WriteAllBytes(bundled, []);
            Assert.Equal(bundled, AudioService.ResolvePath("", config, bundled));
            File.WriteAllBytes(config, []);
            Assert.Equal(config, AudioService.ResolvePath(null, config, bundled));
            Assert.Equal(selected, AudioService.ResolvePath(selected, config, bundled));
        }
        finally { File.Delete(config); File.Delete(bundled); Directory.Delete(folder); }
    }

    private sealed class Playback : IAudioPlayback
    {
        public float VolumeValue;
        public float Volume { set => VolumeValue = value; }
        public int Plays, Stops, Disposals;
        public void Play() => Plays++;
        public void Stop() => Stops++;
        public void Dispose() => Disposals++;
    }
    [Fact]
    public void PrepareStartMuteResetStopUnloadReleaseExactlyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"); File.WriteAllBytes(path, []);
        try
        {
            var created = new List<Playback>();
            var service = new AudioService(_ => { var p = new Playback(); created.Add(p); return p; }, ex => throw ex);
            service.Prepare(path, .3f, false); Assert.Equal(0, created[0].Plays);
            service.PlayPrepared(); Assert.Equal(1, created[0].Plays); Assert.Equal(.3f, created[0].VolumeValue);
            service.SetVolume(.8f, true); Assert.Equal(0, created[0].VolumeValue);
            service.Start(path, .8f, false); Assert.Equal(1, created[0].Stops); Assert.Equal(1, created[0].Disposals);
            service.Stop(); service.Dispose(); service.Dispose(); Assert.Equal(1, created[1].Disposals);
            service.Start(path, 1, false); Assert.Equal(2, created.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingMusicNeverOpensADevice()
    {
        using var service = new AudioService(_ => throw new Exception("Must not open"), ex => throw ex);
        service.Start(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"), 1, false);
        Assert.Contains("未找到", service.Status);
    }

    [Fact]
    public void DecoderFailureIsReportedWithoutEscaping()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav"); File.WriteAllBytes(path, []);
        try
        {
            Exception? error = null;
            using var service = new AudioService(_ => throw new InvalidDataException("bad file"), ex => error = ex);
            service.Start(path, 1, false); Assert.IsType<InvalidDataException>(error); Assert.Contains("不可用", service.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoopFillsSeveralPassesAndHonorsBufferOffset()
    {
        using var source = new RawSource([1, 2, 3, 4]); using var loop = new LoopStream(source);
        var bytes = new byte[14]; Assert.Equal(12, loop.Read(bytes, 1, 12));
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 1, 2, 3, 4, 1, 2, 3, 4, 0 }, bytes);
    }

    [Fact]
    public void EmptyLoopTerminates()
    {
        using var source = new RawSource([]); using var loop = new LoopStream(source);
        Assert.Equal(0, loop.Read(new byte[100], 0, 100));
    }

    private sealed class RawSource(byte[] data) : WaveStream
    {
        public override WaveFormat WaveFormat => new(44100, 16, 1);
        public override long Length => data.Length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var length = Math.Min(count, data.Length - (int)Position);
            Array.Copy(data, Position, buffer, offset, length); Position += length; return length;
        }
    }
}
