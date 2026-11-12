using NAudio.Wave;
using Xunit;

namespace QBounce.Tests;

public sealed class AudioDeviceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RealWindowsWavAndMp3LoopStartResetStopAndRelease(bool mp3)
    {
        var wav = Path.Combine(Path.GetTempPath(), "qbounce-test-" + Guid.NewGuid() + ".wav");
        var encoded = Path.ChangeExtension(wav, ".mp3");
        try
        {
            // Self-generated 80 ms silence; no downloaded music, no audible output.
            using (var writer = new WaveFileWriter(wav, new WaveFormat(44100, 16, 2)))
                writer.Write(new byte[14112], 0, 14112);
            if (mp3)
            {
                using var source = new WaveFileReader(wav);
                MediaFoundationEncoder.EncodeToMp3(source, encoded, 128000);
            }
            var path = mp3 ? encoded : wav;
            var errors = new List<Exception>();
            var outputs = new List<NAudioPlayback>();
            using (var service = new AudioService(p => { var device = new NAudioPlayback(p); outputs.Add(device); return device; }, errors.Add))
            {
                service.Start(path, 0, true);
                Assert.Empty(errors); Assert.Equal("循环播放", service.Status);
                Thread.Sleep(220); // exceed the file duration, exercising the actual looping decoder/output
                Assert.Null(outputs[0].Error); Assert.Equal(PlaybackState.Playing, outputs[0].State);
                service.Start(path, 0, true); Assert.Empty(errors);
                service.Stop(); Assert.Equal("已停止", service.Status);
            }
            // Windows denies this exclusive open if the decoder still owns a file handle.
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(wav); if (File.Exists(encoded)) File.Delete(encoded); }
    }
}
